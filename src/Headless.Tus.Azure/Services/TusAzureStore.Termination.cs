// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using tusdotnet.Interfaces;

namespace Headless.Tus.Services;

public sealed partial class TusAzureStore : ITusTerminationStore
{
    public async Task DeleteFileAsync(string fileId, CancellationToken cancellationToken)
    {
        var blobClient = _GetBlobClient(fileId);

        bool deleted;

        try
        {
            var response = await blobClient.DeleteIfExistsAsync(
                DeleteSnapshotsOption.IncludeSnapshots,
                cancellationToken: cancellationToken
            );

            deleted = response.Value;
        }
        catch (Exception e)
        {
            _logger.BlobDeleteFailed(e, blobClient.Name);
            deleted = false;
        }

        if (deleted)
        {
            _logger.FileDeleted(fileId);
        }
        else
        {
            _logger.FileNotFoundForDeletion(fileId);
        }
    }
}

internal static partial class TusAzureStoreTerminationLog
{
    [LoggerMessage(EventId = 3212, Level = LogLevel.Error, Message = "Failed to delete blob: {BlobName}")]
    public static partial void BlobDeleteFailed(this ILogger logger, Exception e, string blobName);

    [LoggerMessage(EventId = 3213, Level = LogLevel.Information, Message = "Deleted file {FileId}")]
    public static partial void FileDeleted(this ILogger logger, string fileId);

    [LoggerMessage(EventId = 3214, Level = LogLevel.Warning, Message = "File {FileId} was not found for deletion")]
    public static partial void FileNotFoundForDeletion(this ILogger logger, string fileId);
}
