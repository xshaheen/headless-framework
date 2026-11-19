// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using tusdotnet.Interfaces;
using tusdotnet.Models;

namespace Headless.Tus.Models;

/// <summary>The readable view of one upload that <c>ITusReadableStore.GetFileAsync</c> returns.</summary>
/// <param name="contentLength">
/// the bytes the content holds: the upload offset, which leaves out a committed chunk still waiting for its
/// checksum-trailer verification
/// </param>
internal sealed class TusAzureFileWrapper(
    TusAzureFile azureFile,
    long contentLength,
    BlobClient blobClient,
    ILogger logger
) : ITusFile
{
    public string Id => azureFile.FileId;

    public async Task<Stream> GetContentAsync(CancellationToken cancellationToken)
    {
        // Bounded to the offset rather than the blob's length, so an unverified tail is never served.
        var excludesUnverifiedTail = contentLength < azureFile.CurrentContentLength;

        // A ranged read cannot ask for zero bytes: when the only committed chunk is unverified, nothing is readable.
        if (excludesUnverifiedTail && contentLength == 0)
        {
            return new MemoryStream([], writable: false);
        }

        try
        {
            var options = new BlobDownloadOptions
            {
                Range = excludesUnverifiedTail ? new HttpRange(0, contentLength) : default,
            };

            var response = await blobClient.DownloadStreamingAsync(options, cancellationToken).ConfigureAwait(false);

            return response.Value.Content;
        }
        catch (Exception e)
        {
            logger.LogFailedToGetContent(e, Id);
            throw;
        }
    }

    public Task<Dictionary<string, Metadata>> GetMetadataAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(azureFile.Metadata.ToTus());
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
    internal static partial void LogFailedToGetContent(this ILogger logger, Exception exception, string fileId);
}
