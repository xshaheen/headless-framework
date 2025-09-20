// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Storage.Blobs.Specialized;
using Framework.Tus.Helpers;
using Framework.Tus.Options;
using Microsoft.Extensions.Logging;

namespace Framework.Tus.Services;

public class AppendBlobHandler
{
    private readonly TusAzureStoreOptions _options;
    private readonly ILogger<AppendBlobHandler> _logger;

    public AppendBlobHandler(TusAzureStoreOptions options, ILogger<AppendBlobHandler> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task CreateBlobAsync(
        AppendBlobClient blobClient,
        Dictionary<string, string> metadata,
        CancellationToken cancellationToken
    )
    {
        var blobHttpHeaders = new Azure.Storage.Blobs.Models.BlobHttpHeaders
        {
            ContentType = "application/octet-stream",
        };
        await blobClient.CreateAsync(blobHttpHeaders, metadata, cancellationToken: cancellationToken);
    }

    public async Task<long> AppendDataAsync(
        AppendBlobClient blobClient,
        Stream data,
        CancellationToken cancellationToken
    )
    {
        var totalBytesWritten = 0L;
        var maxChunkSize = _options.AppendBlobMaxChunkSize;

        if (_options.EnableChunkSplitting)
        {
            // Split large chunks into Azure-compatible sizes
            await foreach (var chunk in ChunkSplitterHelper.SplitStreamAsync(data, maxChunkSize, cancellationToken))
            {
                using (chunk)
                {
                    await blobClient.AppendBlockAsync(chunk, cancellationToken: cancellationToken);
                    totalBytesWritten += chunk.Length;
                }
            }
        }
        else
        {
            // Direct append - will fail if chunk is too large
            await blobClient.AppendBlockAsync(data, cancellationToken: cancellationToken);
            totalBytesWritten = data.Length;
        }

        _logger.LogDebug("Appended {BytesWritten} bytes to append blob", totalBytesWritten);
        return totalBytesWritten;
    }

    public async Task<long> GetUploadOffsetAsync(AppendBlobClient blobClient, CancellationToken cancellationToken)
    {
        try
        {
            var properties = await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken);
            return properties.Value.ContentLength;
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            return 0;
        }
    }
}
