// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Tus.Internal;

/// <summary>
/// The state object of an incomplete upload, stored as JSON at the upload's <c>info</c> key. Its ETag fences every
/// write: a request replaces it only with <c>If-Match</c> on the ETag it read, so a writer whose view is stale cannot
/// overwrite another request's progress.
/// </summary>
internal sealed class TusS3UploadState
{
    /// <summary>The schema this build writes. A reader refuses a newer one instead of dropping fields it cannot see.</summary>
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>The size of every part except the last, fixed when the upload is created.</summary>
    public int PartSize { get; set; }

    /// <summary>The declared upload length, or <see langword="null"/> while the client defers it.</summary>
    public long? UploadLength { get; set; }

    /// <summary>The client's Upload-Metadata header value verbatim, because HEAD must echo it as the client sent it.</summary>
    public string? Metadata { get; set; }

    public TusS3ConcatInfo? Concat { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>The multipart upload the parts belong to, created when the first part is written.</summary>
    public string? MultipartUploadId { get; set; }

    /// <summary>
    /// When the multipart upload was created, so the store can tell that a provider abort window has removed its parts.
    /// </summary>
    public DateTimeOffset? MultipartUploadCreatedAt { get; set; }

    public List<TusS3CommittedPart> Parts { get; set; } = [];

    public List<TusS3CommittedSegment> Segments { get; set; } = [];

    /// <summary>The first part number no request has claimed yet.</summary>
    public int NextPartNumber { get; set; } = 1;

    /// <summary>
    /// The number of durable bytes: the committed parts plus the committed segments. Stored rather than summed so HEAD
    /// reads one value, and every write that changes the parts or segments sets it in the same conditional write.
    /// </summary>
    public long Offset { get; set; }

    public TusS3UploadStatus Status { get; set; } = TusS3UploadStatus.Active;

    /// <summary>
    /// While <see cref="TusS3UploadStatus.Completing"/> or <see cref="TusS3UploadStatus.Committing"/>, the offset
    /// reported to the client: the offset before the final PATCH, so a client whose final PATCH was interrupted re-sends
    /// it and the completion finishes.
    /// </summary>
    public long? PreFinalOffset { get; set; }

    /// <summary>
    /// The upload entered <see cref="TusS3UploadStatus.Completing"/> because the client declared a length equal to the
    /// offset, so no final PATCH exists to roll back to and the length still reads as deferred until it finishes.
    /// </summary>
    public bool CompletingFromDeclaredLength { get; set; }

    /// <summary>The expiry the upload had before it entered <see cref="TusS3UploadStatus.Completing"/>.</summary>
    public DateTimeOffset? ExpiresAtBeforeCompleting { get; set; }

    /// <summary>
    /// The sliding expiration window, learned from the expiry tusdotnet sets right after an append. Once known, each
    /// append's commit moves the expiry itself, so the refresh that follows the append needs no write of its own.
    /// <see langword="null"/> under absolute expiration, which tusdotnet never refreshes after an append.
    /// </summary>
    public TimeSpan? SlidingExpirationWindow { get; set; }

    public byte[] ToJson()
    {
        return JsonSerializer.SerializeToUtf8Bytes(this, TusS3JsonContext.Default.TusS3UploadState);
    }

    /// <summary>Reads a state object from its stored JSON.</summary>
    /// <exception cref="InvalidDataException">The JSON is not a state object this build can read.</exception>
    public static TusS3UploadState FromJson(ReadOnlySpan<byte> json)
    {
        var state =
            TusS3JsonContext.Read(json, TusS3JsonContext.Default.TusS3UploadState)
            ?? throw new InvalidDataException("The tus upload state object is empty.");

        TusS3JsonContext.EnsureReadableSchema(state.SchemaVersion, CurrentSchemaVersion);

        return state;
    }
}

internal enum TusS3UploadStatus
{
    /// <summary>The upload accepts appends.</summary>
    Active = 0,

    /// <summary>The final bytes are durable and one request owns turning them into the finished object.</summary>
    Completing = 1,

    /// <summary>The upload's parts are gone (the provider aborted its multipart upload); it is reported as missing.</summary>
    Gone = 2,

    /// <summary>
    /// The finished object is written and one request is recording it; only the completed record and the removal of
    /// the working set remain. Reads as <see cref="Completing"/> to clients, but the expiry sweep never claims it, so
    /// the sweep cannot delete a finished object whose completion is about to report success.
    /// </summary>
    Committing = 3,
}

internal enum TusS3ConcatKind
{
    Partial = 0,
    Final = 1,
}

/// <summary>The upload's role in a tus concatenation.</summary>
/// <param name="Kind">Whether the upload is a partial or a final upload.</param>
/// <param name="PartialFileIds">A final upload's partial uploads, in order; <see langword="null"/> for a partial.</param>
internal sealed record TusS3ConcatInfo(TusS3ConcatKind Kind, string[]? PartialFileIds = null);

/// <summary>A part uploaded to the multipart upload and recorded as durable.</summary>
internal sealed record TusS3CommittedPart(int Number, string ETag, long Size);

/// <summary>A tail object holding bytes that do not fill a part yet, at its position in the upload.</summary>
internal sealed record TusS3CommittedSegment(string Key, long Offset, long Length);

/// <summary>
/// What a completed upload keeps beside its finished object, so HEAD and the readable store can answer without the
/// working set, which completion deletes.
/// </summary>
internal sealed class TusS3CompletedRecord
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public long UploadLength { get; set; }

    /// <summary>The client's Upload-Metadata header value verbatim.</summary>
    public string? Metadata { get; set; }

    public TusS3ConcatInfo? Concat { get; set; }

    public byte[] ToJson()
    {
        return JsonSerializer.SerializeToUtf8Bytes(this, TusS3JsonContext.Default.TusS3CompletedRecord);
    }

    /// <summary>Reads a completed record from its stored JSON.</summary>
    /// <exception cref="InvalidDataException">The JSON is not a completed record this build can read.</exception>
    public static TusS3CompletedRecord FromJson(ReadOnlySpan<byte> json)
    {
        var record =
            TusS3JsonContext.Read(json, TusS3JsonContext.Default.TusS3CompletedRecord)
            ?? throw new InvalidDataException("The tus completed record is empty.");

        TusS3JsonContext.EnsureReadableSchema(record.SchemaVersion, CurrentSchemaVersion);

        return record;
    }
}
