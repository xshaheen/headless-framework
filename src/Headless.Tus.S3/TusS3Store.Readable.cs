// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Tus.Internal;
using tusdotnet.Interfaces;
using tusdotnet.Models;

namespace Headless.Tus;

public sealed partial class TusS3Store
{
    /// <summary>
    /// Returns an upload's metadata and, once it is complete, its content; <see langword="null"/> when the upload is
    /// unknown or gone.
    /// </summary>
    /// <param name="fileId">the TUS file identifier</param>
    /// <param name="cancellationToken">token to cancel the operation</param>
    /// <returns>the file, or <see langword="null"/> for an unknown or gone upload</returns>
    /// <remarks>
    /// The metadata of an incomplete upload is readable, as it is from the Azure store. Its content is not: until the
    /// upload completes its bytes sit in multipart parts, which S3 does not let anyone read, so
    /// <see cref="ITusFile.GetContentAsync"/> throws for it. Inside tusdotnet's <c>OnFileCompleteAsync</c> the upload
    /// is already complete and the content streams the finished object.
    /// </remarks>
    /// <exception cref="TusStoreException">The id is invalid.</exception>
    public async Task<ITusFile?> GetFileAsync(string fileId, CancellationToken cancellationToken)
    {
        await _EnsureValidFileIdAsync(fileId).ConfigureAwait(false);

        var upload = await _stateStore.LoadAsync(fileId, cancellationToken).ConfigureAwait(false);

        return upload switch
        {
            { Completed: { } record } => new TusS3File(
                fileId,
                record.Metadata,
                isComplete: true,
                _s3,
                _options,
                _keys,
                _logger
            ),
            { State: { Status: not TusS3UploadStatus.Gone } state } => new TusS3File(
                fileId,
                state.Metadata,
                isComplete: false,
                _s3,
                _options,
                _keys,
                _logger
            ),
            _ => null,
        };
    }
}
