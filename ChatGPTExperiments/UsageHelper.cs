using AI.Dev.OpenAI.GPT;
using OpenAI_API.ChatCompletions;

namespace KuinoxSemiAGI
{
    public static class UsageHelper
    {
        public static uint GetTokenCountForMessages( IEnumerable<ChatMessage> messages )
        {
            int total = 2; // every reply is primed with <im_start>assistant
            foreach( var message in messages )
            {
                total += 4;
                if( message.Name is not null ) // if there's a name, the role is omitted
                {
                    total += GPT3Tokenizer.Encode( message.Name ).Count; // role is always required and always 1 token
                    total -= 1;
                }
                total += GPT3Tokenizer.Encode( message.Role ).Count;
                total += GPT3Tokenizer.Encode( message.Content ).Count;
            }
            return (uint)total;
        }
    }
}
