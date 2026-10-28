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
}
