using Azure.AI.OpenAI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AoaiImageAnalyzer.Services;

namespace AoaiImageAnalyzer
{
    internal class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IAoaiService _aoaiService;
        private readonly IHostApplicationLifetime _hostApplicationLifetime;
        private readonly IConfiguration _config;
        private readonly IImageService _imageService;

        public Worker(
            IAoaiService aoaiService, IImageService imageService, 
            IConfiguration config, ILogger<Worker> logger, IHostApplicationLifetime hostApplicationLifetime)
        {
            _aoaiService = aoaiService;
            _imageService = imageService;
            _config = config;
            _logger = logger;
            _hostApplicationLifetime = hostApplicationLifetime;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await DoWork(stoppingToken);
                Environment.ExitCode = 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred.");
                Environment.ExitCode = 1;
            }
            finally
            {
                _hostApplicationLifetime.StopApplication();
            }
        }

        private async Task DoWork(CancellationToken stoppingToken)
        {
            var promptFile = _config["prompt-file"];
            _logger.LogTrace("prompt file is {promptFile}", promptFile);
            if (string.IsNullOrWhiteSpace(promptFile) || !File.Exists(promptFile))
            {
                throw new ApplicationException("システムプロンプトが読み込めません");
            }
            var prompt = await File.ReadAllTextAsync(promptFile);
            _logger.LogTrace("system prompt is {systemPrompt}", prompt);


            var imageFile = _config["image-file"];
            _logger.LogTrace("image file is {imageFile}", imageFile);
            if (string.IsNullOrWhiteSpace(imageFile) || !File.Exists(imageFile))
            {
                throw new ApplicationException("画像が読み込めません");
            }


            _logger.LogInformation("Uploading image ...");
            var bloburi = await _imageService.UploadAsync(File.OpenRead(imageFile), Path.GetFileName(imageFile));


            _logger.LogInformation("Sending chat completion request ...");
            var ccopt = _aoaiService.CreatePayload();
            ccopt.Messages.Add(new ChatRequestSystemMessage(prompt));
            ccopt.Messages.Add(new ChatRequestUserMessage(
                new ChatMessageImageContentItem(bloburi, ChatMessageImageDetailLevel.High)));


            var outputFile = Path.Combine(
                Path.GetDirectoryName(imageFile)!.ToString(),
                string.Format("{0:yyyyMMdd-HHmmss}.md", DateTime.UtcNow));
            _logger.LogInformation("result will be output to {outputFile}", outputFile);
            using var sw = new StreamWriter(File.Create(outputFile), System.Text.Encoding.UTF8);


            if (_config.GetValue<bool>("enable-stream"))
            {
                _logger.LogInformation("streaming chat completion ...");
                try
                {
                    await foreach (var token in _aoaiService.StreamChatCompletionAsync(ccopt))
                    {
                        await sw.WriteAsync(token);
                        Console.Write(token);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "streaming failed.");
                }
                finally
                {
                    await sw.FlushAsync();
                }

                _logger.LogInformation("streaming completed.");
                return;
            }
            else
            {
                _logger.LogInformation("sending chat completed.");
                var task = _aoaiService.GetChatCompletionsAsync(ccopt);
                while (!task.IsCompleted)
                {
                    await Task.Delay(100);
                    Console.Write(">");
                }
                var comp = task.Result.Value;
                Console.WriteLine();
                _logger.LogInformation("chat completed.");

                _logger.LogInformation("Usage: {totalTokens} = {promptTokens} + {completionTokens}",
                    comp.Usage.TotalTokens, comp.Usage.PromptTokens, comp.Usage.CompletionTokens);
                foreach (var choice in comp.Choices)
                {
                    await sw.WriteAsync(choice.Message.Content);
                }
                await sw.FlushAsync();
            }

        }
    }
}
