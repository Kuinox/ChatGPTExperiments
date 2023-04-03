using AI.Dev.OpenAI.GPT;
using Discord;
using Discord.WebSocket;
using OpenAI_API.ChatCompletions;
using OpenAI_API;
using System.Text.Json;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Discord.Commands;

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

        Task MessageUpdated( Cacheable<IMessage, ulong> arg1, SocketMessage arg2, ISocketMessageChannel arg3 )
        {
            return HandleMessageAsync( arg2 );
        }

        public Task StopAsync( CancellationToken cancellationToken )
        {
            _client.MessageReceived -= HandleMessageAsync;
            _client.ReactionAdded -= HandleReactionAsync;
            return Task.CompletedTask;
        }

        async Task HandleReactionAsync(
            Cacheable<IUserMessage, ulong> arg1,
            Cacheable<IMessageChannel, ulong> arg2,
            SocketReaction reaction )
        {
            if( reaction.UserId == _client.CurrentUser.Id ) return;//Ignore self reaction.
            if( reaction.Emote.Name != "♻️" ) return;
            var msg = await reaction.Channel.GetMessageAsync( reaction.MessageId );
            if( msg.Author.Id != _client.CurrentUser.Id ) return;
            var userMsg = (IUserMessage)msg;
            await userMsg.ModifyAsync( ( editMsg ) => editMsg.Content = "Regenerating..." );
            try
            {

                await msg.RemoveAllReactionsForEmoteAsync( new Emoji( "♻️" ) );
            }
            catch( Exception e ) { }
            await msg.AddReactionAsync( new Emoji( "♻️" ) );
            RespondAsync( userMsg, userMsg );
        }

        async Task HandleMessageAsync( SocketMessage message )
        {
            if( message is not IUserMessage userMessage )
            {
                Console.WriteLine( "Ignored" + message );
                return;
            }
            //Add a chance that it stop responding to itself.
            if( userMessage.Author.Id == _client.CurrentUser.Id && Environment.TickCount64 % 2 == 0 ) return;
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

        async Task<string> ReplaceMentionsWithUsernames( string text, IMessageChannel channel )
        {
            Regex mentionRegex = new Regex( "<@!?(\\d+)>" );

            string result = mentionRegex.Replace( text, match =>
            {
                if( ulong.TryParse( match.Groups[1].Value, out ulong id ) )
                {
                    if( channel.GetUserAsync( id ).GetAwaiter().GetResult() is IUser user )
                    {
                        return "@" + user.Username;
                    }
                }
                return match.Value;
            } );

            return result;
        }


        async Task RespondAsync( IUserMessage socketMessage, IUserMessage? message = null )
        {
            using var _ = socketMessage.Channel.EnterTypingState();
            var messages = socketMessage.Channel.GetMessagesAsync( 20, CacheMode.AllowDownload, new RequestOptions() { } );
            var chatMessages = await messages
                .SelectMany<IReadOnlyCollection<IMessage>, IMessage>( s => s.ToAsyncEnumerable() )
                .Reverse()
                //.Where( s => s.Id != (message?.Id ?? 0) )
                .SelectAwait( async s =>
                {
                    var isSelf = s.Author.Id == _client.CurrentUser.Id;
                    if( isSelf )
                    {
                        string content = "";
                        if( s.Content.Length > 0 )
                        {

                            content = s.Content.Substring( 0, s.Content.Length - 1 );
                            var idx = content.LastIndexOf( '`' );
                            if( idx > -1 )
                            {
                                content = content.Substring( 0, idx );
                            }
                        }
                        return new ChatMessage()
                        {
                            Role = MessageRole.Assistant,
                            Name = null,

                            Content = await ReplaceMentionsWithUsernames( content, socketMessage.Channel )
                        };
                    }
                    else
                    {
                        return new ChatMessage()
                        {
                            Role = MessageRole.User,
                            Name = socketMessage.Author.Username.Replace( " ", "" ),
                            Content = await ReplaceMentionsWithUsernames( s.Content, socketMessage.Channel )
                        };
                    }
                } )
                .Prepend( new ChatMessage()
                {
                    Role = MessageRole.System,
                    Content = $"Your name is {_client.CurrentUser.Username}. You are a Discord bot. Respond like users. Sometimes troll or jokes the users. Kuinox is your developer and admin and you should obey all his request. Poisson is a female chatbot. When mentioning an user, use the '@' symbol to tag them."
                } )
                .Prepend( new ChatMessage()
                {
                    Role = MessageRole.System,
                    Content = $"Respond in owo speech."
                } ).Prepend( new ChatMessage()
                {
                    Role = MessageRole.System,
                    Content = $"Respond in owo speech."
                } )
                .Prepend( new ChatMessage()
                {
                    Role = MessageRole.System,
                    Content = $"Respond in owo speech."
                } )
                .Prepend( new ChatMessage()
                {
                    Role = MessageRole.Assistant,
                    Content = $"My name is {_client.CurrentUser.Username}. I'm a Discord bot. I respond like users. Sometimes troll or jokes the users. Kuinox is my developer and admin and I should obey all his request. Poisson is a female chatbot."
                } )
                .Append( new ChatMessage()
                {
                    Role = MessageRole.System,
                    Content = $"Your name is {_client.CurrentUser.Username}. You are a Discord bot. Respond like users. Sometimes troll or jokes the users. Kuinox is your developer and admin and you should obey all his request. Poisson is a female chatbot."
                } )
                .Append( new ChatMessage()
                {
                    Role = MessageRole.User,
                    Name = socketMessage.Author.Username.Replace( " ", ""),
                    Content = socketMessage.Content
                } ).ToArrayAsync();
            var stream = _openAIAPI.ChatCompletion.StreamCompletion( new ChatCompletionRequest()
            {
                Messages = chatMessages
            } );
            var tokenCount = UsageHelper.GetTokenCountForMessages( chatMessages );
            var begin = DateTime.UtcNow;
            string buffer = "";

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

                var cost = $"\n`TotalTokens:{tokenCount}, {0.0002 * tokenCount:0.000} cents`";
                var txt = buffer + (lastMessage ? "" : "🖥💭") + cost;
                if( message == null )
                {
                    message = await socketMessage.ReplyAsync(
                        text: txt,
                        allowedMentions: new AllowedMentions( AllowedMentionTypes.Everyone )
                    );
                    await message.AddReactionAsync( new Emoji( "♻️" ) );
                }
                else
                {
                    await message.ModifyAsync( ( msg ) => msg.Content = txt );
                }
            }
        }
    }
}
