using Microsoft.Extensions.Azure;

using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using Azure.Storage.Blobs.Specialized;

namespace AoaiImageAnalyzer.Services
{
    internal interface IImageService
    {
        Task<Uri> UploadAsync(System.IO.Stream source, string fileName);
    }

    internal class ImageService : IImageService
    {
        private readonly IConfiguration _config;
        private readonly ILogger<ImageService> _logger;
        private readonly BlobServiceClient _blobServiceClient;

        public ImageService(IConfiguration config, ILogger<ImageService> logger, BlobServiceClient blobServiceClient)
        {
            _config = config.GetSection("AzureStorage");
            _logger = logger;
            _blobServiceClient = blobServiceClient;
        }
        public async Task<Uri> UploadAsync(Stream source, string fileName)
        {
            var containerName = _config["Container"];
            if(string.IsNullOrWhiteSpace(containerName))
                throw new ApplicationException("Container name is not set");

            var container = _blobServiceClient.GetBlobContainerClient(containerName);
            var blobName = Guid.NewGuid().ToString();
            _logger.LogTrace("Uploading {fileName} as {blobName} to {containerName} container", fileName, blobName, containerName);
            await container.UploadBlobAsync(blobName, source);

            var blob = container.GetBlobClient(blobName);
            await blob.SetMetadataAsync(new Dictionary<string, string>
            {
                { "OriginalName", fileName }
            });

            var uri = GenerateBlobSas(blob);
            _logger.LogTrace("Download url is {uri}", uri);
            return uri;
        }

        private Uri GenerateBlobSas(BlobClient blob)
        {
            if(!blob.CanGenerateSasUri)
                throw new ApplicationException("Blob cannot generate SAS");

            var builder = new BlobSasBuilder()
            {
                BlobContainerName = blob.GetParentBlobContainerClient().Name,
                BlobName = blob.Name,
                Resource = "b0",
                ExpiresOn = DateTimeOffset.UtcNow.AddHours(1),
            };
            builder.SetPermissions(BlobSasPermissions.Read);
            return blob.GenerateSasUri(builder);

        }
    }

    internal static class ImageServiceExtensions
    {
        public static IServiceCollection AddImageService(this IServiceCollection services, IConfiguration config)
        {
            services.AddAzureClients(builder =>
            {
                var constr = config["AzureStorage:ConnectionString"]!.ToString();
                builder.AddBlobServiceClient(constr);
            });
            services.AddSingleton<IImageService, ImageService>();
            return services;
        }
    }
}
