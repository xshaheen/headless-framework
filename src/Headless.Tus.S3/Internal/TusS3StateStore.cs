// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;

namespace Headless.Tus.Internal;

/// <summary>
/// Reads and writes an upload's state object and completed record, with every state write conditional on the ETag the
/// request holds.
/// </summary>
internal sealed class TusS3StateStore(
    IAmazonS3 s3,
    TusS3StoreOptions options,
    TusS3Keys keys,
    TusS3RequestMemo memo,
    ILogger logger
)
{
    private const string _JsonContentType = "application/json";

    /// <summary>
    /// Loads an upload: its state when it is incomplete, its completed record when it is finished, or
    /// <see langword="null"/> when neither exists. An incomplete upload costs one request and a completed one two.
    /// </summary>
    public async Task<TusS3LoadedUpload?> LoadAsync(string fileId, CancellationToken cancellationToken)
    {
        if (memo.TryGet(fileId, out var memoized))
        {
            return memoized;
        }

        TusS3LoadedUpload upload;
        var info = await _TryGetAsync(keys.Info(fileId), cancellationToken).ConfigureAwait(false);

        if (info is not null)
        {
            upload = TusS3LoadedUpload.Incomplete(TusS3UploadState.FromJson(info.Value.Content), info.Value.ETag);
        }
        else
        {
            var record = await _TryGetAsync(keys.CompletedRecord(fileId), cancellationToken).ConfigureAwait(false);

            if (record is null)
            {
                return null;
            }

            upload = TusS3LoadedUpload.Finished(TusS3CompletedRecord.FromJson(record.Value.Content));
        }

        memo.Set(fileId, upload);

        return upload;
    }

    /// <summary>
    /// Reads an incomplete upload's state straight from storage, bypassing the request memo, or returns
    /// <see langword="null"/> when no state object exists.
    /// </summary>
    /// <exception cref="InvalidDataException">The stored state is not one this build can read.</exception>
    public async Task<TusS3LoadedUpload?> ReadIncompleteAsync(string fileId, CancellationToken cancellationToken)
    {
        var info = await _TryGetAsync(keys.Info(fileId), cancellationToken).ConfigureAwait(false);

        return info is null
            ? null
            : TusS3LoadedUpload.Incomplete(TusS3UploadState.FromJson(info.Value.Content), info.Value.ETag);
    }

    /// <summary>Drops what this request remembers about an upload, after its objects were removed.</summary>
    public void Forget(string fileId)
    {
        memo.Remove(fileId);
    }

    /// <summary>Writes a new upload's state, failing if any state already exists under its id.</summary>
    /// <exception cref="AmazonS3Exception">An upload with the same id already exists (412).</exception>
    public async Task CreateAsync(string fileId, TusS3UploadState state, CancellationToken cancellationToken)
    {
        var etag = await _PutAsync(keys.Info(fileId), state.ToJson(), PutCondition.Create, cancellationToken)
            .ConfigureAwait(false);

        // Creation-with-upload runs its append in the same request, which then needs no read.
        memo.Set(fileId, TusS3LoadedUpload.Incomplete(state, etag));
    }

    /// <summary>
    /// Replaces the upload's state with <paramref name="upload"/>'s, only if storage still holds the ETag the upload
    /// was loaded or last written with.
    /// </summary>
    /// <returns>the upload as written, carrying the ETag the next write of this request must send</returns>
    /// <exception cref="AmazonS3Exception">
    /// Another request wrote the state after this one read it (412 or 409), or removed it (404). The exception is not
    /// translated: tusdotnet answers it with a 500, which tus clients retry by asking HEAD for the committed offset,
    /// whereas a <c>TusStoreException</c> would become a 400 that clients treat as fatal.
    /// </exception>
    public async Task<TusS3LoadedUpload> UpdateAsync(
        string fileId,
        TusS3LoadedUpload upload,
        CancellationToken cancellationToken
    )
    {
        var state = upload.State ?? throw new InvalidOperationException("A completed upload has no state to update.");

        try
        {
            var etag = await _PutAsync(
                    keys.Info(fileId),
                    state.ToJson(),
                    PutCondition.Match(upload.ETag!),
                    cancellationToken
                )
                .ConfigureAwait(false);

            var written = TusS3LoadedUpload.Incomplete(state, etag);
            memo.Set(fileId, written);

            return written;
        }
        catch (Exception e)
        {
            // The caller changed the memoized state for a write that did not land, so the next call must re-read.
            memo.Remove(fileId);

            if (e is AmazonS3Exception s3Exception && IsStaleWrite(s3Exception))
            {
                logger.LogS3StaleWriteRejected(fileId);
            }

            throw;
        }
    }

    /// <summary>Writes the record a completed upload keeps, failing if one already exists.</summary>
    public async Task CreateCompletedRecordAsync(
        string fileId,
        TusS3CompletedRecord record,
        CancellationToken cancellationToken
    )
    {
        await _PutAsync(keys.CompletedRecord(fileId), record.ToJson(), PutCondition.Create, cancellationToken)
            .ConfigureAwait(false);

        memo.Set(fileId, TusS3LoadedUpload.Finished(record));
    }

    /// <summary>
    /// Records a finished upload: writes its completed record, then deletes its state object. Deleting the state is the
    /// commit point, because readers fall back to the record only once the state is gone.
    /// </summary>
    /// <remarks>
    /// The record is written unconditionally. Every request that finishes the same upload derives it from the same
    /// completing state, so a second writer stores identical content, and a crash between the two writes is repaired
    /// by the next attempt simply writing the record again.
    /// </remarks>
    public async Task CommitCompletionAsync(
        string fileId,
        TusS3CompletedRecord record,
        CancellationToken cancellationToken
    )
    {
        await _PutAsync(keys.CompletedRecord(fileId), record.ToJson(), PutCondition.None, cancellationToken)
            .ConfigureAwait(false);

        await s3.DeleteObjectAsync(
                new DeleteObjectRequest { BucketName = options.Bucket, Key = keys.Info(fileId) },
                cancellationToken
            )
            .ConfigureAwait(false);

        memo.Set(fileId, TusS3LoadedUpload.Finished(record));
    }

    /// <summary>
    /// Marks the upload gone, unless another request already moved it on (finished it, or marked it gone itself), in
    /// which case that request meets the same missing parts itself.
    /// </summary>
    /// <returns>the upload as written, or <paramref name="upload"/> when the write lost to another request</returns>
    public async Task<TusS3LoadedUpload> TryMarkGoneAsync(
        string fileId,
        TusS3LoadedUpload upload,
        CancellationToken cancellationToken
    )
    {
        upload.State!.Status = TusS3UploadStatus.Gone;

        try
        {
            return await UpdateAsync(fileId, upload, cancellationToken).ConfigureAwait(false);
        }
        catch (AmazonS3Exception e) when (IsStaleWrite(e) || e.StatusCode is HttpStatusCode.NotFound)
        {
            return upload;
        }
    }

    /// <summary>
    /// Whether a conditional write failed because storage no longer holds the version the writer read. Matched by
    /// status and error code: the SDK does not throw a dedicated exception type for these.
    /// </summary>
    public static bool IsStaleWrite(AmazonS3Exception exception)
    {
        return exception.StatusCode switch
        {
            HttpStatusCode.PreconditionFailed => true,
            // S3 answers 409 when a concurrent conditional write to the same key is still in flight.
            HttpStatusCode.Conflict => exception.HasErrorCode(TusS3ErrorCodes.ConditionalRequestConflict),
            // If-Match on a key another writer deleted (completion or termination won the race).
            HttpStatusCode.NotFound => exception.HasErrorCode(TusS3ErrorCodes.NoSuchKey),
            _ => false,
        };
    }

    private async Task<string> _PutAsync(
        string key,
        byte[] content,
        PutCondition condition,
        CancellationToken cancellationToken
    )
    {
        await using var body = new MemoryStream(content, writable: false);

        var request = new PutObjectRequest
        {
            BucketName = options.Bucket,
            Key = key,
            InputStream = body,
            AutoCloseStream = false,
            ContentType = _JsonContentType,
        };

        if (condition.IsCreate)
        {
            request.IfNoneMatch = "*";
        }
        else if (condition.ETag is not null)
        {
            request.IfMatch = condition.ETag;
        }

        TusS3RequestPresets.Apply(request, options.Provider);

        var response = await s3.PutObjectAsync(request, cancellationToken).ConfigureAwait(false);

        return response.ETag;
    }

    private readonly record struct PutCondition(bool IsCreate, string? ETag)
    {
        public static PutCondition None => default;

        public static PutCondition Create => new(IsCreate: true, ETag: null);

        public static PutCondition Match(string etag) => new(IsCreate: false, etag);
    }

    private async Task<(byte[] Content, string ETag)?> _TryGetAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await s3.GetObjectAsync(
                    new GetObjectRequest { BucketName = options.Bucket, Key = key },
                    cancellationToken
                )
                .ConfigureAwait(false);

            // A state object is small, so sizing the buffer from the declared length avoids regrowing it, and a buffer
            // filled exactly is handed over without the copy ToArray makes.
            var capacity = response.ContentLength is > 0 and <= int.MaxValue ? (int)response.ContentLength : 0;
            await using var buffer = new MemoryStream(capacity);
            await response.ResponseStream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);

            var content = buffer.Length == capacity && capacity > 0 ? buffer.GetBuffer() : buffer.ToArray();

            return (content, response.ETag);
        }
        catch (AmazonS3Exception e) when (e.StatusCode is HttpStatusCode.NotFound)
        {
            return null;
        }
    }
}

/// <summary>An upload as one read found it: either incomplete, with its state and ETag, or completed.</summary>
internal sealed class TusS3LoadedUpload
{
    private TusS3LoadedUpload(TusS3UploadState? state, string? etag, TusS3CompletedRecord? completed)
    {
        State = state;
        ETag = etag;
        Completed = completed;
    }

    /// <summary>The state of an incomplete upload; <see langword="null"/> when <see cref="Completed"/> is set.</summary>
    public TusS3UploadState? State { get; }

    /// <summary>The ETag the state was read or last written with, the fence for the next write.</summary>
    public string? ETag { get; }

    /// <summary>The record of a completed upload; <see langword="null"/> when <see cref="State"/> is set.</summary>
    public TusS3CompletedRecord? Completed { get; }

    /// <summary>
    /// When this request's append committed the state, so an expiry refresh later in the same request can tell how far
    /// past the commit tusdotnet set it; <see langword="null"/> when no append committed in this request.
    /// </summary>
    public DateTimeOffset? AppendCommittedAt { get; set; }

    public static TusS3LoadedUpload Incomplete(TusS3UploadState state, string etag)
    {
        return new(state, etag, completed: null);
    }

    public static TusS3LoadedUpload Finished(TusS3CompletedRecord record)
    {
        return new(state: null, etag: null, record);
    }
}
