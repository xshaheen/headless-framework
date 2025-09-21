// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;
using tusdotnet.Interfaces;
using tusdotnet.Models;

namespace Framework.Tus.Models;

public sealed class TusAzureFileWrapper(TusAzureFile azureFile, BlobClient blobClient, ILogger logger) : ITusFile
{
    public string Id => azureFile.FileId;

    public async Task<Stream> GetContentAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await blobClient.DownloadStreamingAsync(cancellationToken: cancellationToken);
            return response.Value.Content;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to get content for file {FileId}", Id);
            throw;
        }
    }

    public async Task<Dictionary<string, Metadata>> GetMetadataAsync(CancellationToken cancellationToken)
    {
        return await Task.FromResult(azureFile.Metadata.ToTus());
    }
}
