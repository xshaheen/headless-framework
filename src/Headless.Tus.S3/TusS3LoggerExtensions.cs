// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

namespace Headless.Tus;

internal static partial class TusS3LoggerExtensions
{
    [LoggerMessage(
        EventId = 3253,
        EventName = "S3BucketCreated",
        Level = LogLevel.Information,
        Message = "Created tus upload bucket {Bucket}"
    )]
    internal static partial void LogS3BucketCreated(this ILogger logger, string bucket);

    [LoggerMessage(
        EventId = 3254,
        EventName = "S3BucketKeptWithForeignObjects",
        Level = LogLevel.Information,
        Message = "Kept tus upload bucket {Bucket} because it holds objects outside the store's prefix"
    )]
    internal static partial void LogS3BucketKeptWithForeignObjects(this ILogger logger, string bucket);

    [LoggerMessage(
        EventId = 3270,
        EventName = "S3FileCreated",
        Level = LogLevel.Information,
        Message = "Created file {FileId} with upload length {UploadLength}"
    )]
    internal static partial void LogS3FileCreated(this ILogger logger, string fileId, long uploadLength);

    [LoggerMessage(
        EventId = 3271,
        EventName = "S3FileCreateFailed",
        Level = LogLevel.Error,
        Message = "Failed to create file with upload length {UploadLength}"
    )]
    internal static partial void LogS3FileCreateFailed(this ILogger logger, Exception e, long uploadLength);

    [LoggerMessage(
        EventId = 3272,
        EventName = "S3UploadLengthSet",
        Level = LogLevel.Debug,
        Message = "Set upload length for file {FileId} to {UploadLength}"
    )]
    internal static partial void LogS3UploadLengthSet(this ILogger logger, string fileId, long uploadLength);

    [LoggerMessage(
        EventId = 3273,
        EventName = "S3UploadLengthSetFailed",
        Level = LogLevel.Error,
        Message = "Failed to set upload length for file {FileId}"
    )]
    internal static partial void LogS3UploadLengthSetFailed(this ILogger logger, Exception e, string fileId);

    [LoggerMessage(
        EventId = 3274,
        EventName = "S3StaleWriteRejected",
        Level = LogLevel.Warning,
        Message = "Rejected a write to file {FileId}: its state changed since this request read it, so another request wrote it concurrently"
    )]
    internal static partial void LogS3StaleWriteRejected(this ILogger logger, string fileId);

    [LoggerMessage(
        EventId = 3275,
        EventName = "S3ExpirationSet",
        Level = LogLevel.Debug,
        Message = "Set expiration for file {FileId} to {Expires}"
    )]
    internal static partial void LogS3ExpirationSet(this ILogger logger, string fileId, DateTimeOffset expires);

    [LoggerMessage(
        EventId = 3276,
        EventName = "S3ExpirationSkipped",
        Level = LogLevel.Debug,
        Message = "Kept no expiration for file {FileId}: it is missing, completed, or completing"
    )]
    internal static partial void LogS3ExpirationSkipped(this ILogger logger, string fileId);

    [LoggerMessage(
        EventId = 3277,
        EventName = "S3ExpirationSetFailed",
        Level = LogLevel.Error,
        Message = "Failed to set expiration for file {FileId}"
    )]
    internal static partial void LogS3ExpirationSetFailed(this ILogger logger, Exception e, string fileId);

    [LoggerMessage(
        EventId = 3278,
        EventName = "S3MultipartUploadLifetimePassed",
        Level = LogLevel.Information,
        Message = "Reported file {FileId} as gone: its multipart upload was created at {CreatedAt}, beyond the provider's abort window"
    )]
    internal static partial void LogS3MultipartUploadLifetimePassed(
        this ILogger logger,
        string fileId,
        DateTimeOffset createdAt
    );

    [LoggerMessage(
        EventId = 3279,
        EventName = "S3AppendCommitted",
        Level = LogLevel.Debug,
        Message = "Appended {Bytes} bytes to file {FileId} as {PartCount} parts and {SegmentCount} tail segments"
    )]
    internal static partial void LogS3AppendCommitted(
        this ILogger logger,
        string fileId,
        long bytes,
        int partCount,
        int segmentCount
    );

    [LoggerMessage(
        EventId = 3280,
        EventName = "S3AppendFailed",
        Level = LogLevel.Error,
        Message = "Failed to append data to file {FileId}"
    )]
    internal static partial void LogS3AppendFailed(this ILogger logger, Exception e, string fileId);

    [LoggerMessage(
        EventId = 3281,
        EventName = "S3ClientDisconnected",
        Level = LogLevel.Information,
        Message = "Client disconnected from file {FileId} after {Bytes} bytes; storing the received bytes"
    )]
    internal static partial void LogS3ClientDisconnected(this ILogger logger, string fileId, long bytes);

    [LoggerMessage(
        EventId = 3282,
        EventName = "S3PartUploadFailed",
        Level = LogLevel.Warning,
        Message = "Failed to upload part {PartNumber} of file {FileId}; storing its bytes and every later byte as tail segments"
    )]
    internal static partial void LogS3PartUploadFailed(this ILogger logger, Exception e, string fileId, int partNumber);

    [LoggerMessage(
        EventId = 3283,
        EventName = "S3SegmentWriteFailed",
        Level = LogLevel.Warning,
        Message = "Failed to write the tail segment of file {FileId} at offset {Offset}; committing only the bytes before it"
    )]
    internal static partial void LogS3SegmentWriteFailed(this ILogger logger, Exception e, string fileId, long offset);

    [LoggerMessage(
        EventId = 3284,
        EventName = "S3SegmentCleanupFailed",
        Level = LogLevel.Warning,
        Message = "Failed to delete {SegmentCount} consumed tail segments of file {FileId}; they remain until the upload is removed"
    )]
    internal static partial void LogS3SegmentCleanupFailed(
        this ILogger logger,
        Exception e,
        string fileId,
        int segmentCount
    );

    [LoggerMessage(
        EventId = 3285,
        EventName = "S3MultipartAbortFailed",
        Level = LogLevel.Warning,
        Message = "Failed to abort the unrecorded multipart upload of file {FileId}; its parts remain until the expiry sweep or the bucket's abort rules collect them"
    )]
    internal static partial void LogS3MultipartAbortFailed(this ILogger logger, Exception e, string fileId);

    [LoggerMessage(
        EventId = 3286,
        EventName = "S3MultipartUploadMissing",
        Level = LogLevel.Warning,
        Message = "Marked file {FileId} as gone: the provider no longer has its multipart upload"
    )]
    internal static partial void LogS3MultipartUploadMissing(this ILogger logger, string fileId);

    [LoggerMessage(
        EventId = 3287,
        EventName = "S3PartNumbersExhausted",
        Level = LogLevel.Warning,
        Message = "Marked file {FileId} as gone: every part number of its multipart upload is used"
    )]
    internal static partial void LogS3PartNumbersExhausted(this ILogger logger, string fileId);

    [LoggerMessage(
        EventId = 3288,
        EventName = "S3UploadCompleted",
        Level = LogLevel.Information,
        Message = "Completed file {FileId} as one object of {UploadLength} bytes"
    )]
    internal static partial void LogS3UploadCompleted(this ILogger logger, string fileId, long uploadLength);

    [LoggerMessage(
        EventId = 3289,
        EventName = "S3CompletionPartsRejected",
        Level = LogLevel.Warning,
        Message = "Marked file {FileId} as gone: the provider refused to assemble its parts ({ErrorCode})"
    )]
    internal static partial void LogS3CompletionPartsRejected(this ILogger logger, string fileId, string errorCode);

    [LoggerMessage(
        EventId = 3290,
        EventName = "S3FinishedObjectAlreadyWritten",
        Level = LogLevel.Debug,
        Message = "Found the finished object of file {FileId} already written by an earlier completion attempt"
    )]
    internal static partial void LogS3FinishedObjectAlreadyWritten(this ILogger logger, string fileId);

    [LoggerMessage(
        EventId = 3291,
        EventName = "S3FailedToGetContent",
        Level = LogLevel.Error,
        Message = "Failed to get the content of file {FileId}"
    )]
    internal static partial void LogS3FailedToGetContent(this ILogger logger, Exception e, string fileId);
}
