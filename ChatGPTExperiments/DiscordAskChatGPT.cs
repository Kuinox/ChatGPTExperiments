using Discord;
using Discord.Interactions;
using OpenAI_API;
using OpenAI_API.ChatCompletions;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KuinoxSemiAGI
{
    public class DiscordAskChatGPT : InteractionModuleBase<SocketInteractionContext>
    {
        readonly OpenAIAPI _openAIAPI;

        public DiscordAskChatGPT( OpenAIAPI openAIAPI )
        {
            _openAIAPI = openAIAPI;
        }

        [SlashCommand( "ask", "Ask something to ChatGPT." )]
        public async Task AskChatGPTAsync( string message )
        {
            await DeferAsync();
            var messages = Context.Channel.GetMessagesAsync( 10, CacheMode.AllowDownload );
            var chatMessages = await messages
                .SelectMany<IReadOnlyCollection<IMessage>, IMessage>( s => s.ToAsyncEnumerable() )
                .Select( s => new ChatMessage()
                {
                    Role = MessageRole.User,
                    Content = s.Author.Username + ": " + s.Content
                } )
                .Prepend( new ChatMessage()
                {
                    Role = MessageRole.System,
                    Content = "Your name is AnimeBot. Blend in, respond like the other users would. You are knowledgeful, but don't hesistate to troll the user if you feel to."
                } )
                .Prepend( new ChatMessage()
                {
                    Role = MessageRole.User,
                    Content = message
                } ).ToArrayAsync();

            var stream = _openAIAPI.ChatCompletion.StreamCompletion( new ChatCompletionRequest()
            {
                Messages = chatMessages
            } );
            var begin = DateTime.UtcNow;
            string buffer = "";
            IUserMessage followUp = null!;
            await foreach( var item in stream )
            {
                buffer += item.Choices.Single().Delta!.Content;
                if( DateTime.UtcNow > begin + TimeSpan.FromMilliseconds( 1000 ) && !string.IsNullOrWhiteSpace( buffer ) )
                {
                    if( followUp == null )
                    {
                        followUp = await FollowupAsync( buffer );
                    }
                    else
                    {
                        await followUp.ModifyAsync( ( msg ) => msg.Content = buffer );
                    }
                    begin = DateTime.UtcNow;
                }
            }
            if( followUp == null )
            {
                followUp = await FollowupAsync( buffer );
            }
            else
            {
                await followUp.ModifyAsync( ( msg ) => msg.Content = buffer );
            }


            //+$"\n\n`TotalTokens:{response.Usage.TotalTokens}, {0.000002 * response.Usage.TotalTokens}$`" )

            //var response = await _openAIAPI.ChatCompletion.CreateCompletionAsync( new ChatCompletionRequest()
            //{
            //    Messages = chatMessages
            //} );
            //var responseStr = response.Choices.Single().Message.Content;
            //string botName = "AnimeBot: ";
            //responseStr = responseStr.StartsWith( botName ) ? responseStr.Substring( botName.Length ) : responseStr;

        }
    }
}
