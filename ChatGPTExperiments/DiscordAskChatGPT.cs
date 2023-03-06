using Discord.Interactions;
using OpenAI_API;
using OpenAI_API.ChatCompletions;

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
            var response = await _openAIAPI.ChatCompletion.CreateCompletionAsync( new ChatCompletionRequest()
            {
                Messages = new[]
                {
                    new ChatMessage()
                    {
                        Role = MessageRole.System,
                        Content = "Blend in, ask like the user would respond. You are knowledgeful but don't hesistate to troll the user if you feel to."
                    },
                    new ChatMessage()
                    {
                        Role = MessageRole.User,
                        Content = message
                    }
                }
            } );
            await FollowupAsync( response.Choices.Single().Message.Content );
        }
    }
}
