// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Tus;

public sealed partial class TusAzureStore
{
    /// <summary>
    /// Returns <see langword="true"/> if a blob for the given TUS file identifier exists in the
    /// configured container.
    /// </summary>
    /// <param name="fileId">the TUS file identifier</param>
    /// <param name="cancellationToken">token to cancel the operation</param>
    /// <returns><see langword="true"/> if the blob exists; <see langword="false"/> otherwise</returns>
    public async Task<bool> FileExistAsync(string fileId, CancellationToken cancellationToken)
    {
        await _EnsureValidFileIdAsync(fileId).ConfigureAwait(false);

        // A properties read rather than an existence check: it costs the same request, and the rest of the
        // tus request's calls are answered from what it loaded.
        var file = await _LoadFileAsync(fileId, cancellationToken).ConfigureAwait(false);

        return file is not null;
    }

    /// <summary>
    /// Returns the total declared upload length for the given TUS file, or
    /// <see langword="null"/> when deferred-length was used and the client has not yet provided
    /// the final size.
    /// </summary>
    /// <param name="fileId">the TUS file identifier</param>
    /// <param name="cancellationToken">token to cancel the operation</param>
    /// <returns>declared upload length in bytes, or <see langword="null"/> if not yet known</returns>
    public async Task<long?> GetUploadLengthAsync(string fileId, CancellationToken cancellationToken)
    {
        await _EnsureValidFileIdAsync(fileId).ConfigureAwait(false);

        var blobInfo = await _LoadFileAsync(fileId, cancellationToken).ConfigureAwait(false);

        return blobInfo?.Metadata.UploadLength;
    }

    /// <summary>
    /// Returns the number of bytes the given TUS file holds that a client may resume from.
    /// </summary>
    /// <param name="fileId">the TUS file identifier</param>
    /// <param name="cancellationToken">token to cancel the operation</param>
    /// <returns>
    /// the verified committed length of the blob in bytes; 0 if the blob does not exist or has no committed
    /// blocks
    /// </returns>
    /// <remarks>
    /// The offset is the blob's <c>ContentLength</c>, which for a block blob counts committed
    /// blocks only, so staged-but-uncommitted blocks (for example, blocks held pending checksum
    /// verification) never advance it. A chunk committed under a declared <c>Upload-Checksum</c>
    /// trailer counts only once its verification succeeds: until then the offset stays at the
    /// chunk's start, so a failed or interrupted verification leaves the upload resumable from there.
    /// Reading it from the properties lets a HEAD answer every store call from one request.
    /// </remarks>
    public async Task<long> GetUploadOffsetAsync(string fileId, CancellationToken cancellationToken)
    {
        await _EnsureValidFileIdAsync(fileId).ConfigureAwait(false);

        var file = await _LoadFileAsync(fileId, cancellationToken).ConfigureAwait(false);

        return file?.VerifiedLength ?? 0;
    }
}
