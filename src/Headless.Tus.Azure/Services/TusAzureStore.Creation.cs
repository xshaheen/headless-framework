// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Tus.Models;
using Microsoft.Extensions.Logging;
using tusdotnet.Interfaces;

namespace Headless.Tus.Services;

public sealed partial class TusAzureStore : ITusCreationStore
{
    /// <summary>
    /// Creates a new TUS upload by writing an empty block blob with upload metadata and returns
    /// the generated file identifier.
    /// </summary>
    /// <param name="uploadLength">
    /// total number of bytes that the client will upload, or <c>-1</c> when the client used the
    /// Creation-Defer-Length extension (tusdotnet passes <c>-1</c> for deferred lengths; it is not
    /// persisted so <c>GetUploadLengthAsync</c> reports <see langword="null"/> until the client
    /// declares the final length)
    /// </param>
    /// <param name="metadata">
    /// raw TUS metadata string from the Upload-Metadata header, or <see langword="null"/> if
    /// the client did not supply metadata
    /// </param>
    /// <param name="cancellationToken">token to cancel the operation</param>
    /// <returns>the unique file identifier assigned to the new upload</returns>
    public async Task<string> CreateFileAsync(long uploadLength, string? metadata, CancellationToken cancellationToken)
    {
        var fileId = await _fileIdProvider.CreateId(metadata).ConfigureAwait(false);

        try
        {
            // Metadata
            var blobMetadata = TusAzureMetadata.FromTus(metadata);

            blobMetadata.DateCreated = _timeProvider.GetUtcNow();
            // tusdotnet passes -1 for Upload-Defer-Length uploads (same contract as TusDiskStore, which
            // skips persisting it). Storing -1 would make GetUploadLengthAsync report a real length, so
            // HEAD would emit "Upload-Length: -1" instead of "Upload-Defer-Length: 1" and the
            // too-much-data guard would reject every PATCH sent before the length is declared.
            blobMetadata.UploadLength = uploadLength >= 0 ? uploadLength : null;

            // Create empty blob with metadata and content type
            // This ensures the blob exists and has the correct metadata from the start
            // The actual data (blocks) will be uploaded in subsequent requests
            var blockBlobClient = _GetBlockBlobClient(fileId);
            await blockBlobClient
                .UploadAsync(
                    content: Stream.Null,
                    httpHeaders: await _blobHttpHeadersProvider
                        .GetBlobHttpHeadersAsync(blobMetadata.ToUser())
                        .ConfigureAwait(false),
                    metadata: blobMetadata.ToAzure(),
                    cancellationToken: cancellationToken
                )
                .ConfigureAwait(false);

            _logger.FileCreated(fileId, uploadLength);

            return fileId;
        }
        catch (Exception e)
        {
            _logger.FileCreateFailed(e, uploadLength);

            throw;
        }
    }

    /// <summary>
    /// Returns the TUS-formatted metadata string for the given file as originally supplied by the
    /// client.
    /// </summary>
    /// <param name="fileId">the TUS file identifier</param>
    /// <param name="cancellationToken">token to cancel the operation</param>
    /// <returns>
    /// the base64-encoded TUS metadata string, or <see langword="null"/> if the file does not
    /// exist or has no user metadata
    /// </returns>
    /// <remarks>
    /// Internal system keys (upload length, expiration, concatenation type, etc.) are excluded
    /// from the returned string; only the original user-supplied key/value pairs are included.
    /// </remarks>
    public async Task<string?> GetUploadMetadataAsync(string fileId, CancellationToken cancellationToken)
    {
        var blobInfo = await _GetTusFileInfoAsync(fileId, cancellationToken).ConfigureAwait(false);

        return blobInfo?.Metadata.ToTusString();
    }
}

internal static partial class TusAzureStoreCreationLog
{
    [LoggerMessage(
        EventId = 3200,
        Level = LogLevel.Information,
        Message = "Created file {FileId} with upload length {UploadLength}"
    )]
    public static partial void FileCreated(this ILogger logger, string fileId, long uploadLength);

    [LoggerMessage(
        EventId = 3201,
        Level = LogLevel.Error,
        Message = "Failed to create file with upload length {UploadLength}"
    )]
    public static partial void FileCreateFailed(this ILogger logger, Exception e, long uploadLength);
}
