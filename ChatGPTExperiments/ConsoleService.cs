using Microsoft.Extensions.Options;
using OpenAI_API;
using OpenAI_API.ChatCompletions;
using OpenAI_API.Models;

namespace ChatGPTExperiments
{
    public class ConsoleService : BackgroundService
    {
        readonly IOptions<OpenAIAPIConfig> _config;
        OpenAIAPI? _api;

        public ConsoleService(IOptions<OpenAIAPIConfig> config)
        {
            _config = config;
        }

        public override Task StartAsync(CancellationToken cancellationToken)
        {
            _api = new OpenAIAPI(_config.Value.ApiKey);
            return base.StartAsync(cancellationToken);
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var line = await Console.In.ReadLineAsync().WaitAsync(stoppingToken);
                    var result = await _api!.ChatCompletion.CreateCompletionAsync(new ChatCompletionRequest()
                    {
                        Model = Model.GPT35Turbo,
                        Messages = new ChatMessage[]
                        {
                            new ChatMessage
                            {
                                Role = "system",
                                Content = "You are casualy conversating with humans."
                            },
                            new ChatMessage
                            {
                                Role = "user",
                                Content = line
                            }
                        }
                    });
                    Console.WriteLine(result.Choices.Single().Message.Content);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            }
        }
    }
}
