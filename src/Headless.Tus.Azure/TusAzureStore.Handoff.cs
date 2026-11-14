// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure;
using Microsoft.Extensions.Logging;
using tusdotnet.Models;

namespace Headless.Tus;

public sealed partial class TusAzureStore : ITusHandoffStore
{
    /// <summary>
    /// Reads where the upload stands between its last byte and its handoff to the application, from the blob.
    /// </summary>
    /// <param name="fileId">the TUS file identifier</param>
    /// <param name="cancellationToken">token to cancel the operation</param>
    /// <returns>
    /// <see cref="TusHandoffState.NotComplete"/> for an unknown id and for an upload that lacks bytes or a
    /// declared length, <see cref="TusHandoffState.Pending"/> for a complete upload that was not handed off, and
    /// <see cref="TusHandoffState.HandedOff"/> once <see cref="MarkHandedOffAsync"/> recorded it
    /// </returns>
    /// <exception cref="TusStoreException">thrown if the file id is invalid</exception>
    public async Task<TusHandoffState> GetHandoffStateAsync(string fileId, CancellationToken cancellationToken)
    {
        await _EnsureValidFileIdAsync(fileId).ConfigureAwait(false);

        var blobInfo = await _GetTusFileInfoAsync(fileId, cancellationToken).ConfigureAwait(false);

        if (blobInfo is null || _IsIncompleteUpload(blobInfo.Metadata.UploadLength, blobInfo.CurrentContentLength))
        {
            return TusHandoffState.NotComplete;
        }

        return blobInfo.Metadata.HandedOff ? TusHandoffState.HandedOff : TusHandoffState.Pending;
    }

    /// <summary>
    /// Records that the complete upload was handed off to the application, as an entry of the blob's metadata.
    /// </summary>
    /// <param name="fileId">the TUS file identifier</param>
    /// <param name="cancellationToken">token to cancel the operation</param>
    /// <remarks>
    /// Marking an upload again does nothing, and neither does marking an upload that no longer exists. Deleting
    /// the upload removes the mark with the blob.
    /// </remarks>
    /// <exception cref="TusStoreException">thrown if the file id is invalid</exception>
    /// <exception cref="InvalidOperationException">thrown if the upload is not complete yet</exception>
    /// <exception cref="RequestFailedException">
    /// thrown with status 412 when another request wrote the blob after this one read it; the mark is not
    /// written rather than overwriting that request's metadata (see the <c>TusAzureStore</c> remarks)
    /// </exception>
    public async Task MarkHandedOffAsync(string fileId, CancellationToken cancellationToken)
    {
        await _EnsureValidFileIdAsync(fileId).ConfigureAwait(false);

        var blobClient = _GetBlobClient(fileId);
        var blobInfo = await _GetTusFileInfoAsync(blobClient, fileId, cancellationToken).ConfigureAwait(false);

        if (blobInfo is null)
        {
            // Whoever took the upload over deleted it, so nothing is left to mark.
            return;
        }

        if (blobInfo.Metadata.HandedOff)
        {
            return;
        }

        if (_IsIncompleteUpload(blobInfo.Metadata.UploadLength, blobInfo.CurrentContentLength))
        {
            // A mark written early would make the upload read as handed off the moment it completes.
            throw new InvalidOperationException(
                $"File {fileId} is not complete yet, so it cannot be recorded as handed off."
            );
        }

        blobInfo.Metadata.HandedOff = true;

        try
        {
            await _UpdateMetadataAsync(blobClient, blobInfo, cancellationToken).ConfigureAwait(false);
        }
        catch (RequestFailedException e) when (e.Status == 404)
        {
            // Deleted between the read and the write: nothing is left to mark.
            return;
        }

        _logger.UploadHandedOff(fileId);
    }
}

internal static partial class TusAzureStoreHandoffLog
{
    [LoggerMessage(EventId = 3309, Level = LogLevel.Debug, Message = "Recorded file {FileId} as handed off")]
    internal static partial void UploadHandedOff(this ILogger logger, string fileId);
}
