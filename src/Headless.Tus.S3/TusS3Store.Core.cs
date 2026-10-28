// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.S3;
using Headless.Tus.Internal;
using tusdotnet.Models.Concatenation;

namespace Headless.Tus;

public sealed partial class TusS3Store
{
    // Expiration writes merge one field into whatever the state holds now, so losing a race to another writer is
    // retried against fresh state instead of failing a PATCH whose bytes already committed.
    private const int _MaxExpirationWriteAttempts = 3;

    // How close a requested expiry may be to the stored one for the refresh to count as already applied.
    private static readonly TimeSpan _ExpirationFoldTolerance = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Returns <see langword="true"/> when the upload exists and can still make progress or be read.
    /// </summary>
    /// <param name="fileId">the TUS file identifier</param>
    /// <param name="cancellationToken">token to cancel the operation</param>
    /// <returns>
    /// <see langword="false"/> for an unknown id, an upload marked gone, or an incomplete upload whose multipart upload
    /// is older than the provider's abort window, because its parts no longer exist
    /// </returns>
    public async Task<bool> FileExistAsync(string fileId, CancellationToken cancellationToken)
    {
        await _EnsureValidFileIdAsync(fileId).ConfigureAwait(false);

        var upload = await _stateStore.LoadAsync(fileId, cancellationToken).ConfigureAwait(false);

        if (upload is null)
        {
            return false;
        }

        if (upload.State is not { } state)
        {
            return true;
        }

        if (state.Status == TusS3UploadStatus.Gone)
        {
            return false;
        }

        if (_MultipartUploadLifetimePassed(state))
        {
            _logger.LogS3MultipartUploadLifetimePassed(fileId, state.MultipartUploadCreatedAt!.Value);

            return false;
        }

        return true;
    }

    /// <summary>
    /// Returns the declared upload length, or <see langword="null"/> while the client defers it.
    /// </summary>
    /// <param name="fileId">the TUS file identifier</param>
    /// <param name="cancellationToken">token to cancel the operation</param>
    /// <returns>the upload length in bytes, or <see langword="null"/> if not yet known or the upload does not exist</returns>
    /// <remarks>
    /// An upload completing because the client declared a length equal to its offset still reports a deferred length,
    /// so the client re-sends the declaring PATCH and that request finishes the completion.
    /// </remarks>
    public async Task<long?> GetUploadLengthAsync(string fileId, CancellationToken cancellationToken)
    {
        await _EnsureValidFileIdAsync(fileId).ConfigureAwait(false);

        var upload = await _stateStore.LoadAsync(fileId, cancellationToken).ConfigureAwait(false);

        if (upload?.Completed is { } completed)
        {
            return completed.UploadLength;
        }

        return upload?.State switch
        {
            null => null,
            { Status: TusS3UploadStatus.Completing, CompletingFromDeclaredLength: true } => null,
            var state => state.UploadLength,
        };
    }

    /// <summary>
    /// Returns the number of bytes durably stored for the upload.
    /// </summary>
    /// <param name="fileId">the TUS file identifier</param>
    /// <param name="cancellationToken">token to cancel the operation</param>
    /// <returns>the committed offset in bytes; 0 if the upload does not exist</returns>
    /// <remarks>
    /// While an upload is completing, this reports the offset from before its final PATCH, so a client whose final
    /// PATCH was interrupted re-sends it and the completion finishes.
    /// </remarks>
    public async Task<long> GetUploadOffsetAsync(string fileId, CancellationToken cancellationToken)
    {
        await _EnsureValidFileIdAsync(fileId).ConfigureAwait(false);

        var upload = await _stateStore.LoadAsync(fileId, cancellationToken).ConfigureAwait(false);

        if (upload?.Completed is { } completed)
        {
            return completed.UploadLength;
        }

        return upload?.State switch
        {
            null => 0,
            { Status: TusS3UploadStatus.Completing, PreFinalOffset: { } preFinalOffset } => preFinalOffset,
            var state => state.Offset,
        };
    }

    /// <summary>
    /// Returns the client's Upload-Metadata header value verbatim.
    /// </summary>
    /// <param name="fileId">the TUS file identifier</param>
    /// <param name="cancellationToken">token to cancel the operation</param>
    /// <returns>the metadata string, or <see langword="null"/> if the upload does not exist or has none</returns>
    public async Task<string?> GetUploadMetadataAsync(string fileId, CancellationToken cancellationToken)
    {
        await _EnsureValidFileIdAsync(fileId).ConfigureAwait(false);

        var upload = await _stateStore.LoadAsync(fileId, cancellationToken).ConfigureAwait(false);

        return upload?.Completed?.Metadata ?? upload?.State?.Metadata;
    }

    /// <summary>
    /// Returns the upload's role in a concatenation.
    /// </summary>
    /// <param name="fileId">the TUS file identifier</param>
    /// <param name="cancellationToken">token to cancel the operation</param>
    /// <returns>
    /// <see cref="FileConcatPartial"/>, <see cref="FileConcatFinal"/>, or <see langword="null"/> for a regular or
    /// unknown upload
    /// </returns>
    public async Task<FileConcat?> GetUploadConcatAsync(string fileId, CancellationToken cancellationToken)
    {
        await _EnsureValidFileIdAsync(fileId).ConfigureAwait(false);

        var upload = await _stateStore.LoadAsync(fileId, cancellationToken).ConfigureAwait(false);
        var concat = upload?.Completed?.Concat ?? upload?.State?.Concat;

        return concat?.Kind switch
        {
            TusS3ConcatKind.Partial => new FileConcatPartial(),
            TusS3ConcatKind.Final => new FileConcatFinal(concat.PartialFileIds ?? []),
            _ => null,
        };
    }

    /// <summary>
    /// Returns when an incomplete upload expires.
    /// </summary>
    /// <param name="fileId">the TUS file identifier</param>
    /// <param name="cancellationToken">token to cancel the operation</param>
    /// <returns>
    /// the expiry, or <see langword="null"/> when none is set, the upload does not exist, or it is completed or
    /// completing
    /// </returns>
    /// <remarks>
    /// A completed upload never expires: tusdotnet answers 404 for an expired upload, which would break HEAD and
    /// termination of a finished file after its sliding window elapsed.
    /// </remarks>
    public async Task<DateTimeOffset?> GetExpirationAsync(string fileId, CancellationToken cancellationToken)
    {
        await _EnsureValidFileIdAsync(fileId).ConfigureAwait(false);

        var upload = await _stateStore.LoadAsync(fileId, cancellationToken).ConfigureAwait(false);

        return upload?.State is { Status: not TusS3UploadStatus.Completing } state ? state.ExpiresAt : null;
    }

    /// <summary>
    /// Records when an incomplete upload expires.
    /// </summary>
    /// <param name="fileId">the TUS file identifier</param>
    /// <param name="expires">the instant after which the upload is considered expired</param>
    /// <param name="cancellationToken">unused; see remarks</param>
    /// <remarks>
    /// A missing, completed, or completing upload keeps no expiry, so the call does nothing for one.
    /// <para>
    /// <paramref name="cancellationToken"/> is deliberately ignored: tusdotnet refreshes the sliding expiration after a
    /// PATCH with the request's token, which is already cancelled when the client paused or disconnected mid-request,
    /// exactly when the committed bytes still need their window extended so the client can resume.
    /// </para>
    /// </remarks>
    /// <exception cref="AmazonS3Exception">
    /// thrown with status 412 when concurrent writers kept changing the state through every retry of the write
    /// </exception>
    public async Task SetExpirationAsync(string fileId, DateTimeOffset expires, CancellationToken cancellationToken)
    {
        await _EnsureValidFileIdAsync(fileId).ConfigureAwait(false);

        try
        {
            for (var attempt = 1; ; attempt++)
            {
                var upload = await _stateStore.LoadAsync(fileId, CancellationToken.None).ConfigureAwait(false);

                if (upload?.State is not { Status: TusS3UploadStatus.Active } state)
                {
                    _logger.LogS3ExpirationSkipped(fileId);

                    return;
                }

                if (!_RefreshExpiration(fileId, upload, state, expires))
                {
                    return;
                }

                try
                {
                    await _stateStore.UpdateAsync(fileId, upload, CancellationToken.None).ConfigureAwait(false);

                    break;
                }
                catch (AmazonS3Exception e)
                    when (TusS3StateStore.IsStaleWrite(e) && attempt < _MaxExpirationWriteAttempts)
                {
                    // Lost the race to another writer; the failed write dropped the memoized state, so the next
                    // attempt re-reads it.
                }
            }

            _logger.LogS3ExpirationSet(fileId, expires);
        }
        catch (Exception e)
        {
            _logger.LogS3ExpirationSetFailed(e, fileId);

            throw;
        }
    }

    /// <summary>
    /// Decides whether tusdotnet's expiry refresh needs a write, and learns the sliding window from the first refresh
    /// that follows an append in the same request.
    /// </summary>
    /// <remarks>
    /// tusdotnet sets the expiry once at creation under both sliding and absolute expiration, and again after every
    /// append only under sliding expiration. So a refresh after this request's append reveals a sliding window, and the
    /// creation call never does. Once the window is stored, the append's commit has already moved the expiry to about
    /// the value tusdotnet asks for, and the refresh is skipped.
    /// </remarks>
    /// <returns><see langword="true"/> when the state changed and needs writing</returns>
    private bool _RefreshExpiration(
        string fileId,
        TusS3LoadedUpload upload,
        TusS3UploadState state,
        DateTimeOffset expires
    )
    {
        // Only a known window means an append already moved the expiry. tusdotnet computes its value from its own clock
        // read a moment after the commit, so the two are close but never exactly equal. Without a window the write
        // goes ahead even when the value is close, because a refresh right after creation would otherwise never get
        // to teach the window.
        if (
            state is { SlidingExpirationWindow: not null, ExpiresAt: { } stored }
            && (expires - stored).Duration() <= _ExpirationFoldTolerance
        )
        {
            _logger.LogS3ExpirationAlreadyFolded(fileId);

            return false;
        }

        if (upload.AppendCommittedAt is { } committedAt && expires > committedAt)
        {
            var window = expires - committedAt;
            state.SlidingExpirationWindow = window;
            _logger.LogS3SlidingExpirationWindowLearned(fileId, window);
        }

        return ApplyExpiration(state, expires);
    }

    /// <summary>
    /// Sets the expiry on a state about to be written. Every write that refreshes an upload's expiry goes through here,
    /// so an append can carry the refresh in its own commit instead of a separate write.
    /// </summary>
    /// <returns><see langword="true"/> when the state changed and needs writing</returns>
    internal static bool ApplyExpiration(TusS3UploadState state, DateTimeOffset expires)
    {
        if (state.ExpiresAt == expires)
        {
            return false;
        }

        state.ExpiresAt = expires;

        return true;
    }

    // Once the provider's abort window has passed, the parts are gone whether or not the store noticed, so the upload
    // can only be reported as missing.
    private bool _MultipartUploadLifetimePassed(TusS3UploadState state)
    {
        return state is { MultipartUploadId: not null, MultipartUploadCreatedAt: { } createdAt }
            && _options.EffectiveMultipartUploadLifetime is { } lifetime
            && _timeProvider.GetUtcNow() >= createdAt + lifetime;
    }
}
