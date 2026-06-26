// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

namespace Headless.Blobs.FileSystem;

internal static partial class FileSystemBlobStorageLoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        EventName = "GettingFileStream",
        Level = LogLevel.Trace,
        Message = "Getting file stream for {Path}"
    )]
    public static partial void LogGettingFileStream(this ILogger logger, string path);

    // A missing blob is a documented-normal outcome (GetBlobInfoAsync / OpenReadStreamAsync return null); log at
    // Debug so it does not pollute error-level telemetry an operator scans for real failures.
    [LoggerMessage(
        EventId = 2,
        EventName = "FileNotFound",
        Level = LogLevel.Debug,
        Message = "No blob found at {Path}"
    )]
    public static partial void LogFileNotFound(this ILogger logger, string path);

    [LoggerMessage(
        EventId = 3,
        EventName = "CopyingFile",
        Level = LogLevel.Trace,
        Message = "Copying {Path} to {TargetPath}"
    )]
    public static partial void LogCopyingFile(this ILogger logger, string path, string targetPath);

    [LoggerMessage(EventId = 4, EventName = "MovingFile", Level = LogLevel.Trace, Message = "Moving blob {Path}")]
    public static partial void LogMovingFile(this ILogger logger, string path);

    [LoggerMessage(
        EventId = 5,
        EventName = "DeletingByPrefix",
        Level = LogLevel.Information,
        Message = "Deleted {FileCount} blobs under prefix {Prefix}"
    )]
    public static partial void LogDeletingByPrefix(this ILogger logger, int fileCount, string? prefix);

    [LoggerMessage(
        EventId = 6,
        EventName = "FailedToDeleteOriginal",
        Level = LogLevel.Error,
        Message = "Failed to delete original blob {Path} after copy, rolling back destination"
    )]
    public static partial void LogFailedToDeleteOriginal(this ILogger logger, Exception exception, string path);

    [LoggerMessage(
        EventId = 7,
        EventName = "FailedToRollbackDestination",
        Level = LogLevel.Error,
        Message = "Failed to roll back destination blob {Path} after a failed move"
    )]
    public static partial void LogFailedToRollbackDestination(this ILogger logger, Exception exception, string path);

    [LoggerMessage(
        EventId = 8,
        EventName = "PathTraversalRejected",
        Level = LogLevel.Warning,
        Message = "Rejected path traversal attempt on {ParamName}: resolved path '{ResolvedPath}' escapes the base directory"
    )]
    public static partial void LogPathTraversalRejected(this ILogger logger, string paramName, string resolvedPath);
}
