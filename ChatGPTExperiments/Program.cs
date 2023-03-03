using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using OpenAI_API;
namespace ChatGPTExperiments
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            var host = new HostBuilder()
                .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    config.AddJsonFile("appsettings.json", optional: false);
                })
                .ConfigureLogging(x =>
                {
                    x.AddSimpleConsole();
                })
                .ConfigureServices((hostContext, services) =>
                {
                    services.Configure<DiscordSocketConfig>(hostContext.Configuration.GetSection("DiscordSocketConfig"))
                        .Configure<OpenAPIConfig>(hostContext.Configuration.GetSection("OpenAPIConfig"))
                        .AddSingleton<DiscordSocketClient>();
                })
                .Build();

            await host.RunAsync();
        }
    }
}
