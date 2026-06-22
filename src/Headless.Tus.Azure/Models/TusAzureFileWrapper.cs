// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;
using tusdotnet.Interfaces;
using tusdotnet.Models;

namespace Headless.Tus.Models;

internal sealed class TusAzureFileWrapper(TusAzureFile azureFile, BlobClient blobClient, ILogger logger) : ITusFile
{
    public string Id => azureFile.FileId;

    public async Task<Stream> GetContentAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await blobClient
                .DownloadStreamingAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return response.Value.Content;
        }
        catch (Exception e)
        {
            logger.LogFailedToGetContent(e, Id);
            throw;
        }
    }

    public async Task<Dictionary<string, Metadata>> GetMetadataAsync(CancellationToken cancellationToken)
    {
        return await Task.FromResult(azureFile.Metadata.ToTus()).ConfigureAwait(false);
    }
}

internal static partial class TusAzureFileWrapperLog
{
    [LoggerMessage(
        EventId = 3244,
        EventName = "FailedToGetContent",
        Level = LogLevel.Error,
        Message = "Failed to get content for file {FileId}"
    )]
    public static partial void LogFailedToGetContent(this ILogger logger, Exception exception, string fileId);
}
