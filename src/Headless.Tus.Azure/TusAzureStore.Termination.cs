// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using tusdotnet.Interfaces;

namespace Headless.Tus;

public sealed partial class TusAzureStore : ITusTerminationStore
{
    /// <summary>
    /// Deletes the blob associated with the given TUS file identifier, including any snapshots.
    /// </summary>
    /// <param name="fileId">the TUS file identifier to delete</param>
    /// <param name="cancellationToken">token to cancel the operation</param>
    /// <remarks>
    /// Succeeds silently if the blob does not exist (<c>DeleteIfExists</c> returns <see langword="false"/>).
    /// A genuine Azure failure (anything other than not-found) is logged and <em>rethrown</em> so the
    /// caller does not mistake a failed deletion for success — a TUS <c>DELETE</c> must not report 204
    /// while the blob persists, and <c>RemoveExpiredFilesAsync</c> must not count an undeleted file.
    /// </remarks>
    public async Task DeleteFileAsync(string fileId, CancellationToken cancellationToken)
    {
        await _EnsureValidFileIdAsync(fileId).ConfigureAwait(false);

        // Unconditional: a tus DELETE runs under the file lock and asks for the upload to go whatever
        // its state, so an ETag condition would only make a legitimate termination fail.
        await _DeleteBlobAsync(_GetBlobClient(fileId), fileId, conditions: null, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes the blob the caller resolved for <paramref name="fileId"/>. A request names the blob by its
    /// ambient tenant; the removal of expired uploads names it by the tenant it found the blob under.
    /// </summary>
    private async Task _DeleteBlobAsync(
        BlobClient blobClient,
        string fileId,
        BlobRequestConditions? conditions,
        CancellationToken cancellationToken
    )
    {
        bool deleted;

        try
        {
            var response = await blobClient
                .DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, conditions, cancellationToken)
                .ConfigureAwait(false);

            deleted = response.Value;
        }
        // A failed delete condition is an expected outcome for the caller that set it, not a fault.
        catch (Exception e) when (conditions is null || e is not RequestFailedException { Status: 412 })
        {
            _logger.BlobDeleteFailed(e, blobClient.Name);

            throw;
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
    internal static partial void BlobDeleteFailed(this ILogger logger, Exception e, string blobName);

    [LoggerMessage(EventId = 3213, Level = LogLevel.Information, Message = "Deleted file {FileId}")]
    internal static partial void FileDeleted(this ILogger logger, string fileId);

    [LoggerMessage(EventId = 3214, Level = LogLevel.Warning, Message = "File {FileId} was not found for deletion")]
    internal static partial void FileNotFoundForDeletion(this ILogger logger, string fileId);
}
