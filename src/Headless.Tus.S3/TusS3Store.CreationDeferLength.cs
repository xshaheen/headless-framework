// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.S3;
using Headless.Checks;
using Headless.Tus.Internal;
using tusdotnet.Models;

namespace Headless.Tus;

public sealed partial class TusS3Store
{
    /// <summary>
    /// Sets the final length of an upload created with the Creation-Defer-Length extension.
    /// </summary>
    /// <param name="fileId">the TUS file identifier</param>
    /// <param name="uploadLength">the total upload size in bytes; must not be negative</param>
    /// <param name="cancellationToken">token to cancel the operation</param>
    /// <remarks>
    /// A length equal to the bytes already stored completes the upload: the same conditional write that records the
    /// length marks it as completing, and this call then assembles the finished object. When that fails, the upload
    /// keeps reporting a deferred length, so the client declares the same length again and that call finishes it.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="uploadLength"/> is negative.</exception>
    /// <exception cref="TusStoreException">
    /// The upload does not exist, is gone or finished, already has a different length, or the length is below the
    /// stored offset or above the largest length its part size allows; or the length completed the upload and the
    /// provider no longer holds its parts.
    /// </exception>
    /// <exception cref="AmazonS3Exception">
    /// Another request wrote the upload's state after this one read it; the write is rejected rather than overwriting
    /// that request's progress (see the <c>TusS3Store</c> remarks).
    /// </exception>
    public async Task SetUploadLengthAsync(string fileId, long uploadLength, CancellationToken cancellationToken)
    {
        Argument.IsPositiveOrZero(uploadLength);
        await _EnsureValidFileIdAsync(fileId).ConfigureAwait(false);

        try
        {
            var upload = await _stateStore.LoadAsync(fileId, cancellationToken).ConfigureAwait(false);

            if (upload is null || upload.State is { Status: TusS3UploadStatus.Gone })
            {
                throw new TusStoreException($"File {fileId} does not exist");
            }

            var state = upload.State ?? throw new TusStoreException($"File {fileId} is already complete");

            if (state.Status is TusS3UploadStatus.Completing or TusS3UploadStatus.Committing)
            {
                // The client re-sends the PATCH that declared the length when completion was interrupted; the
                // completing state already holds that length, so the request goes on to finish the completion.
                if (state.CompletingFromDeclaredLength && state.UploadLength == uploadLength)
                {
                    await _FinishCompletionAsync(fileId, upload).ConfigureAwait(false);

                    return;
                }

                throw new TusStoreException($"File {fileId} is already complete");
            }

            if (state.UploadLength is { } declared)
            {
                if (declared == uploadLength)
                {
                    return;
                }

                throw new TusStoreException(
                    string.Create(CultureInfo.InvariantCulture, $"File {fileId} already has upload length {declared}")
                );
            }

            if (uploadLength < state.Offset)
            {
                throw new TusStoreException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Upload length {uploadLength} is below the {state.Offset} bytes already uploaded"
                    )
                );
            }

            var cap = TusS3PartSize.DeferredLengthCap(state.PartSize, _options.PartClaimWindow);

            if (uploadLength > cap)
            {
                throw new TusStoreException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Upload length {uploadLength} exceeds the {cap}-byte limit of an upload created without a length"
                    )
                );
            }

            state.UploadLength = uploadLength;

            if (uploadLength == state.Offset)
            {
                state.Status = TusS3UploadStatus.Completing;
                state.PreFinalOffset = state.Offset;
                state.CompletingFromDeclaredLength = true;
                state.ExpiresAtBeforeCompleting = state.ExpiresAt;
            }

            var written = await _stateStore.UpdateAsync(fileId, upload, cancellationToken).ConfigureAwait(false);

            _logger.LogS3UploadLengthSet(fileId, uploadLength);

            if (state.Status == TusS3UploadStatus.Completing)
            {
                await _FinishCompletionAsync(fileId, written).ConfigureAwait(false);
            }
        }
        catch (Exception e)
        {
            _logger.LogS3UploadLengthSetFailed(e, fileId);

            throw;
        }
    }
}
