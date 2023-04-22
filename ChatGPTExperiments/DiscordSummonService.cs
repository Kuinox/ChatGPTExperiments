using Discord;
using Discord.WebSocket;
using System.Text.Json;
using System.Diagnostics;
using System.Text;

namespace KuinoxSemiAGI
{
    public class DiscordSummonService : IHostedService
    {
        readonly DiscordSocketClient _client;

        public DiscordSummonService( DiscordSocketClient client )
        {
            _client = client;
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
                _ = RespondAsync( userMessage ); // We fire and forget tasks because this blocks responding to someone else.
                // Ugly hack, but it works.
                return;
            }

            if( message.Reference?.MessageId.IsSpecified ?? false )
            {
                var replyTo = await message.Channel.GetMessageAsync( message.Reference.MessageId.Value );
                if( replyTo.Author.Id == _client.CurrentUser.Id )
                {
                    _= RespondAsync( userMessage );
                    return;
                }
            }
        }

        async Task RespondAsync( IUserMessage socketMessage, IUserMessage? message = null )
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
