using Discord;
using Discord.Addons.Hosting;
using Discord.WebSocket;
using KuinoxSemiAGI;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using OpenAI_API;
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
                    var openapiKey = hostContext.Configuration.GetSection( "OpenAPI" ).Get<OpenAIAPIConfig>().ApiKey;
                    services
                        .Configure<DiscordHostConfiguration>( hostContext.Configuration.GetSection( "Discord" ) )
                        .AddSingleton( ( s ) => new OpenAIAPI( openapiKey ) )
                        .AddHttpClient()
                        .AddHostedService<InteractionHandler>()
                        .AddHostedService<DiscordSummonService>()                        ;
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
