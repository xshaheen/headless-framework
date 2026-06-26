// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

namespace Headless.Blobs.FileSystem;

internal static partial class FileSystemBlobStorageLoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        EventName = "DeletingFilesMatching",
        Level = LogLevel.Information,
        Message = "Deleting files matching {SearchPattern}"
    )]
    public static partial void LogDeletingFilesMatching(this ILogger logger, string searchPattern);

    [LoggerMessage(EventId = 2, EventName = "DeletingFile", Level = LogLevel.Trace, Message = "Deleting {Path}")]
    public static partial void LogDeletingFile(this ILogger logger, string path);

    [LoggerMessage(
        EventId = 3,
        EventName = "FinishedDeletingFiles",
        Level = LogLevel.Trace,
        Message = "Finished deleting {FileCount} files matching {SearchPattern}"
    )]
    public static partial void LogFinishedDeletingFiles(this ILogger logger, int fileCount, string searchPattern);

    [LoggerMessage(
        EventId = 4,
        EventName = "DeletingDirectory",
        Level = LogLevel.Information,
        Message = "Deleting {Directory} directory"
    )]
    public static partial void LogDeletingDirectory(this ILogger logger, string directory);

    [LoggerMessage(
        EventId = 5,
        EventName = "FinishedDeletingDirectory",
        Level = LogLevel.Trace,
        Message = "Finished deleting {Directory} with {FileCount} files"
    )]
    public static partial void LogFinishedDeletingDirectory(this ILogger logger, string directory, int fileCount);

    [LoggerMessage(
        EventId = 6,
        EventName = "RenamingFile",
        Level = LogLevel.Trace,
        Message = "Renaming {Path} to {NewPath}"
    )]
    public static partial void LogRenamingFile(this ILogger logger, string path, string newPath);

    [LoggerMessage(
        EventId = 7,
        EventName = "CopyingFile",
        Level = LogLevel.Trace,
        Message = "Copying {Path} to {TargetPath}"
    )]
    public static partial void LogCopyingFile(this ILogger logger, string path, string targetPath);

    [LoggerMessage(
        EventId = 8,
        EventName = "GettingFileStream",
        Level = LogLevel.Trace,
        Message = "Getting file stream for {Path}"
    )]
    public static partial void LogGettingFileStream(this ILogger logger, string path);

    // A missing blob is a documented-normal outcome (GetBlobInfoAsync returns null); log at Debug so it does not
    // pollute error-level telemetry an operator scans for real failures.
    [LoggerMessage(
        EventId = 9,
        EventName = "FileNotFound",
        Level = LogLevel.Debug,
        Message = "No blob found at {Path}"
    )]
    public static partial void LogFileNotFound(this ILogger logger, string path);

    [LoggerMessage(
        EventId = 10,
        EventName = "ReturningEmptyFileList",
        Level = LogLevel.Trace,
        Message = "Returning empty file list matching {SearchPattern}: Directory Not Found"
    )]
    public static partial void LogReturningEmptyFileList(this ILogger logger, string searchPattern);

    [LoggerMessage(
        EventId = 11,
        EventName = "GettingFileList",
        Level = LogLevel.Trace,
        Message = "Getting file list matching {SearchPattern} Page: {Page}, PageSize: {PageSize}"
    )]
    public static partial void LogGettingFileList(this ILogger logger, string searchPattern, int page, int pageSize);

    [LoggerMessage(
        EventId = 12,
        EventName = "PathTraversalRejected",
        Level = LogLevel.Warning,
        Message = "Rejected path traversal attempt on {ParamName}: resolved path '{ResolvedPath}' escapes the base directory"
    )]
    public static partial void LogPathTraversalRejected(this ILogger logger, string paramName, string resolvedPath);
}
