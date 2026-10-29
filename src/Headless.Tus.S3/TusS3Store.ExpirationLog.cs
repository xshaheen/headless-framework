// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

namespace Headless.Tus;

internal static partial class TusS3StoreExpirationLog
{
    [LoggerMessage(
        EventId = 3292,
        EventName = "S3FileDeleted",
        Level = LogLevel.Information,
        Message = "Deleted file {FileId}"
    )]
    internal static partial void LogS3FileDeleted(this ILogger logger, string fileId);

    [LoggerMessage(
        EventId = 3293,
        EventName = "S3FileDeleteFailed",
        Level = LogLevel.Error,
        Message = "Failed to delete file {FileId}"
    )]
    internal static partial void LogS3FileDeleteFailed(this ILogger logger, Exception e, string fileId);

    [LoggerMessage(
        EventId = 3294,
        EventName = "S3ExpiredUploadChangedBeforeRemoval",
        Level = LogLevel.Information,
        Message = "Skipped removing expired file {FileId}: it changed after it was found expired"
    )]
    internal static partial void LogS3ExpiredUploadChangedBeforeRemoval(this ILogger logger, string fileId);

    [LoggerMessage(
        EventId = 3295,
        EventName = "S3ExpiredUploadRemoveFailed",
        Level = LogLevel.Error,
        Message = "Failed to remove expired file {FileId}"
    )]
    internal static partial void LogS3ExpiredUploadRemoveFailed(this ILogger logger, Exception e, string fileId);

    [LoggerMessage(
        EventId = 3296,
        EventName = "S3ExpiredUploadsRemoved",
        Level = LogLevel.Information,
        Message = "Removed {RemovedCount} expired files"
    )]
    internal static partial void LogS3ExpiredUploadsRemoved(this ILogger logger, int removedCount);

    [LoggerMessage(
        EventId = 3297,
        EventName = "S3ExpiredUploadsScanFailed",
        Level = LogLevel.Error,
        Message = "Failed to list expired files"
    )]
    internal static partial void LogS3ExpiredUploadsScanFailed(this ILogger logger, Exception e);

    [LoggerMessage(
        EventId = 3298,
        EventName = "S3OrphanWorkingSetRemoved",
        Level = LogLevel.Information,
        Message = "Removed {ObjectCount} leftover objects of file {FileId}, whose state object is gone"
    )]
    internal static partial void LogS3OrphanWorkingSetRemoved(this ILogger logger, string fileId, int objectCount);

    [LoggerMessage(
        EventId = 3299,
        EventName = "S3UnrecordedMultipartUploadAborted",
        Level = LogLevel.Information,
        Message = "Aborted multipart upload {UploadId} of file {FileId}, which its state does not record"
    )]
    internal static partial void LogS3UnrecordedMultipartUploadAborted(
        this ILogger logger,
        string fileId,
        string uploadId
    );

    [LoggerMessage(
        EventId = 3262,
        EventName = "S3UnreadableStateSkipped",
        Level = LogLevel.Warning,
        Message = "Skipped file {FileId} during expired-upload cleanup: its state object cannot be read"
    )]
    internal static partial void LogS3UnreadableStateSkipped(this ILogger logger, Exception e, string fileId);

    [LoggerMessage(
        EventId = 3263,
        EventName = "S3UnrecordedMultipartUploadsScanFailed",
        Level = LogLevel.Warning,
        Message = "Failed to list multipart uploads for leaks during expired-upload cleanup"
    )]
    internal static partial void LogS3UnrecordedMultipartUploadsScanFailed(this ILogger logger, Exception e);

    [LoggerMessage(
        EventId = 3264,
        EventName = "S3SlidingExpirationWindowLearned",
        Level = LogLevel.Debug,
        Message = "File {FileId} uses a sliding expiration of {Window}; appends now carry the refresh in their commit"
    )]
    internal static partial void LogS3SlidingExpirationWindowLearned(
        this ILogger logger,
        string fileId,
        TimeSpan window
    );

    [LoggerMessage(
        EventId = 3265,
        EventName = "S3ExpirationAlreadyFolded",
        Level = LogLevel.Debug,
        Message = "Expiration of file {FileId} is already current; no write needed"
    )]
    internal static partial void LogS3ExpirationAlreadyFolded(this ILogger logger, string fileId);
}
