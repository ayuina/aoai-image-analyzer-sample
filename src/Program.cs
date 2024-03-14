using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using AoaiImageAnalyzer.Services;

namespace AoaiImageAnalyzer // Note: actual namespace depends on the project name.
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            var builder = Host.CreateApplicationBuilder(args);

            builder.Configuration.AddUserSecrets<Program>();
            builder.Services.AddHostedService<Worker>();
            builder.Services.AddAoaiServices(builder.Configuration);
            builder.Services.AddImageService(builder.Configuration);

            await builder.Build().RunAsync();
        }
    }
}