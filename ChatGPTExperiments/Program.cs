using CatBox.NET;
using Discord;
using Discord.Addons.Hosting;
using Discord.WebSocket;
using KuinoxSemiAGI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using System.Xml.Xsl;

namespace ChatGPTExperiments
{
    internal class Program
    {
        static async Task Main( string[] args )
        {
            var host = new HostBuilder()
                .ConfigureAppConfiguration( ( hostingContext, config ) =>
                {
                    config.AddJsonFile( "appsettings.json", optional: false );
                } )
                .ConfigureLogging( x =>
                {
                    x.AddConsole();
                } )
                .ConfigureServices( ( hostContext, services ) =>
                {
                    services
                        .Configure<DiscordHostConfiguration>( hostContext.Configuration.GetSection( "Discord" ) )
                        .Configure<ImageServiceConfig>(hostContext.Configuration.GetSection("ImageService"))
                        .AddHttpClient()
                        .AddHostedService<InteractionHandler>()
                        .AddHostedService<DiscordSummonService>()
                        .AddCatBoxServices( f => f.CatBoxUrl = new Uri( "https://catbox.moe/user/api.php" ) )
                        ;
                } )
                .ConfigureDiscordHost( ( context, config ) =>
                {

                } )
                .UseInteractionService( ( context, config ) =>
                {
                    config.UseCompiledLambda = true;
                } )
                .UseCommandService( ( context, config ) =>
                {
                } )
                .Build();

            await host.RunAsync();
        }
    }
}
