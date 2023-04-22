using Discord;
using Discord.Interactions;
using Microsoft.AspNetCore.Mvc.Routing;
using System.Net.Http;
using Color = Discord.Color;

namespace KuinoxSemiAGI
{
    public class NukeService : InteractionModuleBase<SocketInteractionContext>
    {
        [SlashCommand( "nuke", "Nuke messages in a channel." )]
        [RequireUserPermission( GuildPermission.Administrator )]
        public async Task Nuke( int count )
        {
            await DeferAsync();
            var msgs = await Context.Channel.GetMessagesAsync( count + 1 )
                .SelectMany<IReadOnlyCollection<IMessage>, IMessage>( s => s.ToAsyncEnumerable() )
                .Skip(1)
                .ToArrayAsync();

            await (Context.Channel as ITextChannel).DeleteMessagesAsync( msgs );
            var builder = new EmbedBuilder()
             .WithTitle( $"Nuked {count} messages" )
             .WithImageUrl( "https://media.tenor.com/jkRrt2SrlMkAAAAC/pepe-nuke.gif" )
             .WithColor( Color.Red );

            await FollowupAsync( embed: builder.Build() );
        }
    }
}
