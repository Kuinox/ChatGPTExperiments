using AI.Dev.OpenAI.GPT;
using Discord;
using Discord.WebSocket;
using OpenAI_API.ChatCompletions;
using OpenAI_API;
using System.Text.Json;

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
            return Task.CompletedTask;
        }

        public Task StopAsync( CancellationToken cancellationToken )
        {
            _client.MessageReceived -= HandleMessageAsync;
            return Task.CompletedTask;
        }

        async Task HandleMessageAsync( SocketMessage message )
        {
            if( message is not SocketUserMessage userMessage ) return;
            if( message.MentionedUsers.Any( s => s.Id == _client.CurrentUser.Id ) )
            {
                await RespondAsync( userMessage );
                return;
            }

            if( message.Reference?.MessageId.IsSpecified ?? false )
            {
                var replyTo = await message.Channel.GetMessageAsync( message.Reference.MessageId.Value );
                if( replyTo.Author.Id == _client.CurrentUser.Id )
                {
                    await RespondAsync( userMessage );
                    return;
                }
            }
        }


        async Task RespondAsync( SocketUserMessage socketMessage )
        {
            using var _ = socketMessage.Channel.EnterTypingState();
            var messages = socketMessage.Channel.GetMessagesAsync( 10, CacheMode.AllowDownload );

            var chatMessages = await messages
                .SelectMany<IReadOnlyCollection<IMessage>, IMessage>( s => s.ToAsyncEnumerable() )
                .Reverse()
                .Select( s =>
                {
                    var isSelf = s.Author.Id == _client.CurrentUser.Id;
                    if( isSelf )
                    {
                        var content = s.Content.Substring( 0, s.Content.Length - 1 );
                        var idx = content.LastIndexOf( '`' );
                        content = content.Substring( 0, idx );
                        return new ChatMessage()
                        {
                            Role = MessageRole.Assistant,
                            Name = null,
                            Content = content
                        };
                    }
                    else
                    {
                        return new ChatMessage()
                        {
                            Role = MessageRole.User,
                            Name = s.Author.Username,
                            Content = s.Content
                        };
                    }
                } )
                .Prepend( new ChatMessage()
                {
                    Role = MessageRole.System,
                    Content = $"Your name is {_client.CurrentUser.Username}. You are a Discord bot. Respond like users. Sometimes troll or jokes the users."
                } )
                .Append( new ChatMessage()
                {
                    Role = MessageRole.User,
                    Name = socketMessage.Author.Username,
                    Content = socketMessage.Content
                } ).ToArrayAsync();
            var stream = _openAIAPI.ChatCompletion.StreamCompletion( new ChatCompletionRequest()
            {
                Messages = chatMessages
            } );
            var tokenCount = UsageHelper.GetTokenCountForMessages( chatMessages );
            var begin = DateTime.UtcNow;
            string buffer = "";
            IUserMessage message = null!;
            await foreach( var item in stream )
            {
                var currStr = item.Choices.Single().Delta!.Content;
                buffer += currStr;
                tokenCount += (uint)GPT3Tokenizer.Encode( currStr ).Count;
                if( DateTime.UtcNow > begin + TimeSpan.FromMilliseconds( 250 ) && !string.IsNullOrWhiteSpace( buffer ) )
                {
                    await UpdateMessageAsync( false );
                    begin = DateTime.UtcNow;
                }
            }
            await UpdateMessageAsync( true );

            async Task UpdateMessageAsync( bool lastMessage )
            {

                var cost = $"\n`TotalTokens:{tokenCount}, {0.002 * tokenCount:0.00} cents`";
                var txt = buffer + (lastMessage ? "" : "🖥💭") + cost;
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
