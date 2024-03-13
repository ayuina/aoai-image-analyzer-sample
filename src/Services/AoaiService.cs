using Azure.AI.OpenAI;
using Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Azure;
using System.ClientModel.Primitives;

namespace AoaiImageAnalyzer.Services
{
    public interface IAoaiService
    {
        ChatCompletionsOptions CreatePayload();
        Task<Response<ChatCompletions>> GetChatCompletionsAsync(ChatCompletionsOptions options);
        IAsyncEnumerable<string> StreamChatCompletionAsync(ChatCompletionsOptions options);
    }

    public class AoaiService : IAoaiService
    {
        private readonly IConfiguration _config;
        private readonly ILogger _logger;
        private readonly OpenAIClient _client;

        public AoaiService(IConfiguration config, ILogger<AoaiService> logger, OpenAIClient client)
        {
            this._config = config.GetSection("AzureOpenAI");
            this._logger = logger;
            this._client = client;
        }

        public ChatCompletionsOptions CreatePayload()
        {
            var payload = new ChatCompletionsOptions()
            {
                DeploymentName = _config["Model"],
                MaxTokens = 4000,
                Temperature = 0.2f,
                NucleusSamplingFactor = 0.1f,
                ChoiceCount = 1,
                AzureExtensionsOptions = CreateAzureChatExtensionsOptions(new AzureChatEnhancementConfiguration()
                {
                    Grounding = new AzureChatGroundingEnhancementConfiguration(true),
                    Ocr = new AzureChatOCREnhancementConfiguration(true)
                })
            };

            return payload;
        }

        private AzureChatExtensionsOptions CreateAzureChatExtensionsOptions(AzureChatEnhancementConfiguration config)
        {
            //TODO: Remove workarround for bug AzureChatExtensionsOptions 
            // https://blog.shibayan.jp/entry/20231220/1703051536
            // https://github.com/Azure/azure-sdk-for-net/issues/40826
            // implement without GetBackingField

            var option = new AzureChatExtensionsOptions();
            var enhancementField = option.GetType()
                .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(fi => fi.FieldType == typeof(AzureChatEnhancementConfiguration))
                .First();
            enhancementField.SetValue(option, config);
            return option;
        }


        public async Task<Response<ChatCompletions>> GetChatCompletionsAsync(ChatCompletionsOptions options)
        {
            var bd = ModelReaderWriter.Write(options);
            _logger.LogTrace(bd.ToString());

            return await _client.GetChatCompletionsAsync(options);
        }

        public async IAsyncEnumerable<string> StreamChatCompletionAsync(ChatCompletionsOptions options)
        {
            var response = await _client.GetChatCompletionsStreamingAsync(options);
            await foreach(var item in response.EnumerateValues())
            {
                yield return item.ContentUpdate;
            }
        }

    }

    public static class AoaiServiceHelper
    {
        public static IServiceCollection AddAoaiServices(this IServiceCollection services, IConfiguration config)
        {
            services.AddAzureClients(builder =>
            {
                var endpoint = config["AzureOpenAI:Endpoint"]!.ToString();
                var key = config["AzureOpenAI:Key"]!.ToString();
                builder
                    .AddOpenAIClient(new Uri(endpoint), new AzureKeyCredential(key))
                    .ConfigureOptions(opt => {
                        opt.Diagnostics.IsLoggingContentEnabled = true;
                        opt.Retry.NetworkTimeout = TimeSpan.FromSeconds(3 * 60);
                    });
            });
            services.AddScoped<IAoaiService, AoaiService>();
            return services;
        }
    }
}
