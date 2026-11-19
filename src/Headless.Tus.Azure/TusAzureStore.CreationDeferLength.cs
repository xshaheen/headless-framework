// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Logging;
using tusdotnet.Interfaces;
using tusdotnet.Models;

namespace Headless.Tus;

public sealed partial class TusAzureStore : ITusCreationDeferLengthStore
{
    /// <summary>
    /// Sets the final upload length on a file that was created with the TUS
    /// Creation-Defer-Length extension (i.e., without an initial size declaration).
    /// </summary>
    /// <param name="fileId">the TUS file identifier</param>
    /// <param name="uploadLength">the total upload size in bytes now known by the client; must not be negative</param>
    /// <param name="cancellationToken">token to cancel the operation</param>
    /// <exception cref="TusStoreException">
    /// thrown if the file id is invalid, the file does not exist, the upload already has a different length, or
    /// <paramref name="uploadLength"/> is below the bytes the upload already holds
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">thrown if <paramref name="uploadLength"/> is negative</exception>
    /// <exception cref="Azure.RequestFailedException">
    /// thrown with status 412 when another request wrote the blob after this one read it; the write
    /// is rejected rather than overwriting that request's data (see the <c>TusAzureStore</c> remarks)
    /// </exception>
    public async Task SetUploadLengthAsync(string fileId, long uploadLength, CancellationToken cancellationToken)
    {
        // A negative value would be persisted and then read back as "unknown length" (defer-length),
        // silently re-deferring an upload the client believes is finalized.
        Argument.IsPositiveOrZero(uploadLength);
        await _EnsureValidFileIdAsync(fileId).ConfigureAwait(false);

        try
        {
            var blobClient = _GetBlobClient(fileId);

            // Check if file exists
            var blobInfo =
                await _LoadFileAsync(blobClient, fileId, cancellationToken).ConfigureAwait(false)
                ?? throw new TusStoreException($"File {fileId} does not exist");

            // tusdotnet refuses a second length over HTTP, but a direct store caller is not held to that. A changed length
            // would move the point at which the upload reads as finished under data the client already sent, so only a
            // repeat of the declared length is accepted, and it writes nothing.
            if (blobInfo.Metadata.UploadLength is { } declared)
            {
                if (declared == uploadLength)
                {
                    return;
                }

                throw new TusStoreException(
                    string.Create(CultureInfo.InvariantCulture, $"File {fileId} already has upload length {declared}")
                );
            }

            // tusdotnet checks only the configured maximum, not the stored offset. A length below the bytes already
            // written could never complete, and the expiry sweep would read the upload as finished and keep it forever.
            if (uploadLength < blobInfo.VerifiedLength)
            {
                throw new TusStoreException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Upload length {uploadLength} is below the {blobInfo.VerifiedLength} bytes already uploaded"
                    )
                );
            }

            blobInfo.Metadata.UploadLength = uploadLength;
            await _UpdateMetadataAsync(blobClient, blobInfo, cancellationToken).ConfigureAwait(false);

            _logger.UploadLengthSet(fileId, uploadLength);
        }
        catch (Exception e)
        {
            _logger.UploadLengthSetFailed(e, fileId);

            throw;
        }
    }
}

internal static partial class TusAzureStoreCreationDeferLengthLog
{
    [LoggerMessage(
        EventId = 3202,
        Level = LogLevel.Debug,
        Message = "Set upload length for file {FileId} to {UploadLength}"
    )]
    internal static partial void UploadLengthSet(this ILogger logger, string fileId, long uploadLength);

    [LoggerMessage(EventId = 3203, Level = LogLevel.Error, Message = "Failed to set upload length for file {FileId}")]
    internal static partial void UploadLengthSetFailed(this ILogger logger, Exception e, string fileId);
}
