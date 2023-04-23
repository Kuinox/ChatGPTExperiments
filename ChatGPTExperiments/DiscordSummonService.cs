using Discord;
using Discord.WebSocket;
using System.Text.Json;
using System.Diagnostics;
using System.Text;
using CatBox.NET;
using System.Net.Http;
using Microsoft.Extensions.Options;

namespace KuinoxSemiAGI
{
    public class DiscordSummonService : IHostedService
    {
        readonly DiscordSocketClient _client;
        readonly ICatBoxClient _catBox;
        readonly IOptions<ImageServiceConfig> _config;
        readonly HttpClient _httpClient;
        public DiscordSummonService( DiscordSocketClient client, ICatBoxClient catBox, IHttpClientFactory httpClientFactory, IOptions<ImageServiceConfig> options )
        {
            _client = client;
            _catBox = catBox;
            _config = options;
            _httpClient = httpClientFactory.CreateClient( "ImgurMigrator" );
        }

        public Task StartAsync( CancellationToken cancellationToken )
        {
            _client.MessageReceived += HandleMessageAsync;
            return Task.CompletedTask;
        }

        public Task StopAsync( CancellationToken cancellationToken )
        {
            _client.MessageReceived -= HandleMessageAsync;
            return Task.CompletedTask;
        }

        async Task HandleMessageAsync( SocketMessage message )
        {
            if( message is not IUserMessage userMessage )
            {
                Console.WriteLine( "Ignored" + message );
                return;
            }
            if( userMessage.Author.Id == _client.CurrentUser.Id ) return;
            if( message.MentionedUsers.Any( s => s.Id == _client.CurrentUser.Id ) )
            {
                _ = LangChainRespondAsync( userMessage ); // We fire and forget tasks because this blocks responding to someone else.
            }
            else if( message.Reference?.MessageId.IsSpecified ?? false )
            {
                var replyTo = await message.Channel.GetMessageAsync( message.Reference.MessageId.Value );
                if( replyTo.Author.Id == _client.CurrentUser.Id )
                {
                    _ = LangChainRespondAsync( userMessage );
                }
            }
            var imgurLinks = ImgurAPI.ExtractMsgLinks( userMessage )
                .Where( s =>
                {
                    var host = new Uri( s ).Host;
                    return host == "i.imgur.com" || host == "imgur.com";
                } )
                .GroupBy( s => ImgurAPI.GetImgurId( s ) )
                .Select( s => s.First() )
                .ToArray();
            foreach( var link in imgurLinks )
            {
                await ImgurMigrate( userMessage, link );
            }

        }

        async Task ImgurMigrate( IUserMessage msg, string link )
        {
            if( ImgurAPI.IsAlbumLink( link ) )
            {
                var album = await ImgurAPI.GetAlbumData( _httpClient, link );
                if( album is null )
                {
                    return;
                }

                var imageLinks = await _catBox.UploadMultipleUrls( new UrlUploadRequest()
                {
                    Files = album.media!.Select( s => new Uri( s.url! ) ),
                    UserHash = _config.Value.CatboxUserHash
                } ).ToArrayAsync();

                var catboxLink = await _catBox.CreateAlbum( new CreateAlbumRequest()
                {
                    Description = $"Migrated from imgur album {link}",
                    Files = imageLinks!,
                    Title = album.title,
                    UserHash = _config.Value.CatboxUserHash
                } );
                await msg.ReplyAsync( Uri.TryCreate( catboxLink, UriKind.Absolute, out _ ) ? $"Imgur is Dead!\nI backed up this album to {catboxLink}." : $"Catbox Upload Error {catboxLink}.", allowedMentions: new AllowedMentions( AllowedMentionTypes.None ) );
            }
            else
            {
                var data = ImgurAPI.IsGalleryLink( link ) ? await ImgurAPI.GetGalleryData( _httpClient, link ) : await ImgurAPI.GetPostData( _httpClient, link );
                if( data is null ) return;
                string url = data!.media!.Single().url!;
                var catboxLink = await _catBox.UploadMultipleUrls( new UrlUploadRequest()
                {
                    // we add .png because 1. imgur return the image but a webpage if there is not file extension.
                    // and because imgur doesn't care of the actual file extension.
                    Files = new[] { new Uri( url ) },
                    UserHash = _config.Value.CatboxUserHash
                } ).SingleAsync();
                await msg.ReplyAsync( Uri.TryCreate( catboxLink, UriKind.Absolute, out _ ) ? $"Imgur is Dead!\nI backed up this to {catboxLink}." : $"Catbox Upload Error {catboxLink}.", allowedMentions: new AllowedMentions( AllowedMentionTypes.None ) );
            }
        }

        async Task LangChainRespondAsync( IUserMessage socketMessage, IUserMessage? message = null )
        {
            using var _ = socketMessage.Channel.EnterTypingState();
            var begin = DateTime.UtcNow;
            string buffer = "";
            var messages = await socketMessage.Channel.GetMessagesAsync( 10 )
                .SelectMany<IReadOnlyCollection<IMessage>, IMessage>( s => s.ToAsyncEnumerable() )
                .Reverse()
                //.SkipWhile( s => (message?.Id ?? 0) != s.Id || socketMessage.Id != s.Id )
                .Select( s => new[] { s!.Author.Username, s.CleanContent } )
                .ToArrayAsync();
            var json = JsonSerializer.Serialize(
                new
                {
                    chat_history = messages,
                    input = socketMessage.Author.Username + ": " + socketMessage.CleanContent
                }
            );
            var userMessageEncoded = Convert.ToBase64String( Encoding.UTF8.GetBytes( json ) );
            Console.WriteLine( json );
            var startInfo = new ProcessStartInfo( "python", "C:/dev/myassistant/kuinox-gpt.py " + userMessageEncoded )
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            var process = Process.Start( startInfo )!;

            process.OutputDataReceived += ( _, args ) =>
            {
                Console.WriteLine( args.Data );
            };

            process.ErrorDataReceived += ( _, args ) =>
            {
                Console.WriteLine( args.Data );
                buffer += args.Data;
            };

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            while( !process.HasExited )
            {
                if( DateTime.UtcNow > begin + TimeSpan.FromMilliseconds( 250 ) && !string.IsNullOrWhiteSpace( buffer ) )
                {
                    await UpdateMessageAsync( false );
                    begin = DateTime.UtcNow;
                }
            }
            await process.WaitForExitAsync();
            await UpdateMessageAsync( true );

            async Task UpdateMessageAsync( bool lastMessage )
            {

                var txt = buffer + (lastMessage ? "" : "🖥💭");
                if( message == null )
                {
                    message = await socketMessage.ReplyAsync(
                        text: txt,
                        allowedMentions: new AllowedMentions( AllowedMentionTypes.Everyone )
                    );
                }
                else
                {
                    await message.ModifyAsync( ( msg ) => msg.Content = txt );
                }
            }
        }
    }
}
