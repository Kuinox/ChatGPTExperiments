using AI.Dev.OpenAI.GPT;
using Discord;
using Discord.WebSocket;
using OpenAI_API.ChatCompletions;
using OpenAI_API;
using System.Text.Json;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Discord.Commands;
using System.Diagnostics;
using System.Text;

namespace KuinoxSemiAGI
{
    public class DiscordSummonService : IHostedService
    {
        readonly DiscordSocketClient _client;
        readonly OpenAIAPI _openAIAPI;

        public DiscordSummonService( DiscordSocketClient client, OpenAIAPI openAIAPI )
        {
            _client = client;
            _openAIAPI = openAIAPI;
        }

        public Task StartAsync( CancellationToken cancellationToken )
        {
            _client.MessageReceived += HandleMessageAsync;
            _client.ReactionAdded += HandleReactionAsync; ;
            _client.MessageUpdated += MessageUpdated;
            return Task.CompletedTask;
        }

        async Task MessageUpdated( Cacheable<IMessage, ulong> arg1, SocketMessage arg2, ISocketMessageChannel arg3 )
        {
            //return HandleMessageAsync( arg2 );
        }

        public Task StopAsync( CancellationToken cancellationToken )
        {
            _client.MessageReceived -= HandleMessageAsync;
            _client.ReactionAdded -= HandleReactionAsync;
            _client.MessageUpdated -= MessageUpdated;
            return Task.CompletedTask;
        }

        async Task HandleReactionAsync(
            Cacheable<IUserMessage, ulong> arg1,
            Cacheable<IMessageChannel, ulong> arg2,
            SocketReaction reaction )
        {
            //if( reaction.UserId == _client.CurrentUser.Id ) return;//Ignore self reaction.
            //if( reaction.Emote.Name != "♻️" ) return;
            //var msg = await reaction.Channel.GetMessageAsync( reaction.MessageId );
            //if( msg.Author.Id != _client.CurrentUser.Id ) return;
        }

        async Task HandleMessageAsync( SocketMessage message )
        {
            if( message is not IUserMessage userMessage )
            {
                Console.WriteLine( "Ignored" + message );
                return;
            }
            //Add a chance that it stop responding to itself.
            if( userMessage.Author.Id == _client.CurrentUser.Id ) return;
            if( message.MentionedUsers.Any( s => s.Id == _client.CurrentUser.Id ) )
            {
                RespondAsync( userMessage );
                return;
            }

            if( message.Reference?.MessageId.IsSpecified ?? false )
            {
                var replyTo = await message.Channel.GetMessageAsync( message.Reference.MessageId.Value );
                if( replyTo.Author.Id == _client.CurrentUser.Id )
                {
                    RespondAsync( userMessage );
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
