// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Headless.Blobs.SshNet;

internal static partial class SftpLoggerExtensions
{
    // === SftpClientPool ===

    [LoggerMessage(
        EventId = 1,
        EventName = "AcquiredPooledClient",
        Level = LogLevel.Trace,
        Message = "Acquired pooled SFTP client"
    )]
    public static partial void LogAcquiredPooledClient(this ILogger logger);

    [LoggerMessage(
        EventId = 2,
        EventName = "AcquiredPooledClientAfterWait",
        Level = LogLevel.Trace,
        Message = "Acquired pooled SFTP client after wait"
    )]
    public static partial void LogAcquiredPooledClientAfterWait(this ILogger logger);

    [LoggerMessage(
        EventId = 3,
        EventName = "PoolFullDisposingExcess",
        Level = LogLevel.Trace,
        Message = "Pool full, disposing excess SFTP client"
    )]
    public static partial void LogPoolFullDisposingExcess(this ILogger logger);

    [LoggerMessage(
        EventId = 4,
        EventName = "ReturnedClientToPool",
        Level = LogLevel.Trace,
        Message = "Returned SFTP client to pool"
    )]
    public static partial void LogReturnedClientToPool(this ILogger logger);

    [LoggerMessage(
        EventId = 5,
        EventName = "CreatingConnection",
        Level = LogLevel.Trace,
        Message = "Creating new SFTP connection to {Host}:{Port}"
    )]
    public static partial void LogCreatingConnection(this ILogger logger, string host, int port);

    [LoggerMessage(
        EventId = 6,
        EventName = "Connected",
        Level = LogLevel.Trace,
        Message = "Connected to {Host}:{Port}, working directory: {WorkingDirectory}"
    )]
    public static partial void LogConnected(this ILogger logger, string host, int port, string workingDirectory);

    [LoggerMessage(
        EventId = 7,
        EventName = "ClientNotConnected",
        Level = LogLevel.Trace,
        Message = "Client not connected, validation failed"
    )]
    public static partial void LogClientNotConnected(this ILogger logger);

    [LoggerMessage(
        EventId = 8,
        EventName = "ClientValidationFailed",
        Level = LogLevel.Debug,
        Message = "Client validation failed, will create new connection"
    )]
    public static partial void LogClientValidationFailed(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 9,
        EventName = "DisconnectingClient",
        Level = LogLevel.Trace,
        Message = "Disconnecting SFTP client from {Host}:{Port}"
    )]
    public static partial void LogDisconnectingClient(this ILogger logger, string host, int port);

    [LoggerMessage(
        EventId = 10,
        EventName = "ErrorDisconnectingClient",
        Level = LogLevel.Debug,
        Message = "Error disconnecting SFTP client"
    )]
    public static partial void LogErrorDisconnectingClient(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 11,
        EventName = "DisposingPool",
        Level = LogLevel.Trace,
        Message = "Disposing SFTP client pool"
    )]
    public static partial void LogDisposingPool(this ILogger logger);

    [LoggerMessage(
        EventId = 12,
        EventName = "PoolDisposed",
        Level = LogLevel.Trace,
        Message = "SFTP client pool disposed"
    )]
    public static partial void LogPoolDisposed(this ILogger logger);

    // === SshBlobStorage ===

    [LoggerMessage(
        EventId = 13,
        EventName = "EnsuringDirectoryExists",
        Level = LogLevel.Trace,
        Message = "Ensuring {Directory} directory exists"
    )]
    public static partial void LogEnsuringDirectoryExists(this ILogger logger, string[] directory);

    [LoggerMessage(
        EventId = 14,
        EventName = "CreatingContainerSegment",
        Level = LogLevel.Information,
        Message = "Creating Container segment {Segment}"
    )]
    public static partial void LogCreatingContainerSegment(this ILogger logger, string segment);

    [LoggerMessage(EventId = 15, EventName = "SavingBlob", Level = LogLevel.Trace, Message = "Saving {Path}")]
    public static partial void LogSavingBlob(this ILogger logger, string path);

    [LoggerMessage(
        EventId = 16,
        EventName = "ErrorSavingBlobCreatingDirectory",
        Level = LogLevel.Debug,
        Message = "Error saving {Path}: Attempting to create directory"
    )]
    public static partial void LogErrorSavingBlobCreatingDirectory(
        this ILogger logger,
        Exception exception,
        string path
    );

    [LoggerMessage(EventId = 17, EventName = "DeletingBlob", Level = LogLevel.Trace, Message = "Deleting {Path}")]
    public static partial void LogDeletingBlob(this ILogger logger, string path);

    [LoggerMessage(
        EventId = 18,
        EventName = "DeleteFileNotFound",
        Level = LogLevel.Error,
        Message = "Unable to delete {Path}: File not found"
    )]
    public static partial void LogDeleteFileNotFound(this ILogger logger, Exception exception, string path);

    [LoggerMessage(
        EventId = 19,
        EventName = "DeletingFilesMatchingPattern",
        Level = LogLevel.Information,
        Message = "Deleting {FileCount} files matching {SearchPattern}"
    )]
    public static partial void LogDeletingFilesMatchingPattern(
        this ILogger logger,
        int fileCount,
        string searchPattern
    );

    [LoggerMessage(
        EventId = 20,
        EventName = "FailedToDeleteFile",
        Level = LogLevel.Warning,
        Message = "Failed to delete {Path}"
    )]
    public static partial void LogFailedToDeleteFile(this ILogger logger, string path);

    [LoggerMessage(
        EventId = 21,
        EventName = "FinishedDeletingFilesMatchingPattern",
        Level = LogLevel.Trace,
        Message = "Finished deleting {FileCount} files matching {SearchPattern}"
    )]
    public static partial void LogFinishedDeletingFilesMatchingPattern(
        this ILogger logger,
        int fileCount,
        string searchPattern
    );

    [LoggerMessage(
        EventId = 22,
        EventName = "DeletingDirectory",
        Level = LogLevel.Information,
        Message = "Deleting {Directory} directory"
    )]
    public static partial void LogDeletingDirectory(this ILogger logger, string directory);

    [LoggerMessage(EventId = 23, EventName = "DeletingFile", Level = LogLevel.Trace, Message = "Deleting file {Path}")]
    public static partial void LogDeletingFile(this ILogger logger, string path);

    [LoggerMessage(
        EventId = 24,
        EventName = "FinishedDeletingDirectory",
        Level = LogLevel.Trace,
        Message = "Finished deleting {Directory} directory with {FileCount} files"
    )]
    public static partial void LogFinishedDeletingDirectory(this ILogger logger, string directory, int fileCount);

    [LoggerMessage(
        EventId = 25,
        EventName = "DeleteDirectoryNotFound",
        Level = LogLevel.Trace,
        Message = "Delete directory not found with {Directory}"
    )]
    public static partial void LogDeleteDirectoryNotFound(this ILogger logger, string directory);

    [LoggerMessage(
        EventId = 26,
        EventName = "RenamingBlob",
        Level = LogLevel.Information,
        Message = "Renaming {Path} to {TargetPath}"
    )]
    public static partial void LogRenamingBlob(this ILogger logger, string path, string targetPath);

    [LoggerMessage(
        EventId = 27,
        EventName = "RemovingExistingForRename",
        Level = LogLevel.Debug,
        Message = "Removing existing {TargetPath} path for rename operation"
    )]
    public static partial void LogRemovingExistingForRename(this ILogger logger, string targetPath);

    [LoggerMessage(
        EventId = 28,
        EventName = "RemovedExistingForRename",
        Level = LogLevel.Debug,
        Message = "Removed existing {TargetPath} path for rename operation"
    )]
    public static partial void LogRemovedExistingForRename(this ILogger logger, string targetPath);

    [LoggerMessage(
        EventId = 29,
        EventName = "ErrorRenamingBlobCreatingDirectory",
        Level = LogLevel.Debug,
        Message = "Error renaming {Path} to {NewPath}: Attempting to create directory"
    )]
    public static partial void LogErrorRenamingBlobCreatingDirectory(
        this ILogger logger,
        Exception exception,
        string path,
        string newPath
    );

    [LoggerMessage(
        EventId = 30,
        EventName = "ErrorRenamingBlob",
        Level = LogLevel.Error,
        Message = "Error renaming {Path} to {NewPath}"
    )]
    public static partial void LogErrorRenamingBlob(
        this ILogger logger,
        Exception exception,
        string path,
        string newPath
    );

    [LoggerMessage(
        EventId = 31,
        EventName = "CopyingBlob",
        Level = LogLevel.Information,
        Message = "Copying {Container}/{Path} to {TargetContainer}/{TargetPath}"
    )]
    public static partial void LogCopyingBlob(
        this ILogger logger,
        string[] container,
        string path,
        string[] targetContainer,
        string targetPath
    );

    [LoggerMessage(
        EventId = 32,
        EventName = "CopySourceNotFound",
        Level = LogLevel.Error,
        Message = "Source file not found: {Container}/{Path}"
    )]
    public static partial void LogCopySourceNotFound(
        this ILogger logger,
        Exception exception,
        string[] container,
        string path
    );

    [LoggerMessage(
        EventId = 33,
        EventName = "ErrorCopyingBlob",
        Level = LogLevel.Error,
        Message = "Error copying {Container}/{Path} to {TargetContainer}/{TargetPath}"
    )]
    public static partial void LogErrorCopyingBlob(
        this ILogger logger,
        Exception exception,
        string[] container,
        string path,
        string[] targetContainer,
        string targetPath
    );

    [LoggerMessage(
        EventId = 34,
        EventName = "CheckingBlobExists",
        Level = LogLevel.Trace,
        Message = "Checking if {Path} exists"
    )]
    public static partial void LogCheckingBlobExists(this ILogger logger, string path);

    [LoggerMessage(
        EventId = 35,
        EventName = "GettingFileStream",
        Level = LogLevel.Trace,
        Message = "Getting file stream for {Path}"
    )]
    public static partial void LogGettingFileStream(this ILogger logger, string path);

    [LoggerMessage(
        EventId = 36,
        EventName = "FileStreamNotFound",
        Level = LogLevel.Error,
        Message = "Unable to get file stream for {Path}: File Not Found"
    )]
    public static partial void LogFileStreamNotFound(this ILogger logger, Exception exception, string path);

    [LoggerMessage(
        EventId = 37,
        EventName = "GettingBlobInfo",
        Level = LogLevel.Trace,
        Message = "Getting blob info for {Path}"
    )]
    public static partial void LogGettingBlobInfo(this ILogger logger, string path);

    [LoggerMessage(
        EventId = 38,
        EventName = "BlobInfoIsDirectory",
        Level = LogLevel.Warning,
        Message = "Unable to get blob info for {Path}: Is a directory"
    )]
    public static partial void LogBlobInfoIsDirectory(this ILogger logger, string path);

    [LoggerMessage(
        EventId = 39,
        EventName = "BlobInfoNotFound",
        Level = LogLevel.Error,
        Message = "Unable to get file info for {Path}: File Not Found"
    )]
    public static partial void LogBlobInfoNotFound(this ILogger logger, Exception exception, string path);

    [LoggerMessage(
        EventId = 40,
        EventName = "GettingBlobsRecursively",
        Level = LogLevel.Trace,
        Message = "Getting blobs recursively matching {Prefix} and {Pattern}..."
    )]
    public static partial void LogGettingBlobsRecursively(this ILogger logger, string prefix, Regex? pattern);

    [LoggerMessage(
        EventId = 41,
        EventName = "GettingFileListRecursively",
        Level = LogLevel.Trace,
        Message = "Getting file list recursively matching {Prefix} and {Pattern}..."
    )]
    public static partial void LogGettingFileListRecursively(this ILogger logger, string prefix, Regex? pattern);

    [LoggerMessage(
        EventId = 42,
        EventName = "CancellationRequested",
        Level = LogLevel.Debug,
        Message = "Cancellation requested"
    )]
    public static partial void LogCancellationRequested(this ILogger logger);

    [LoggerMessage(
        EventId = 43,
        EventName = "DirectoryNotFound",
        Level = LogLevel.Debug,
        Message = "Directory not found with {PathPrefix}"
    )]
    public static partial void LogDirectoryNotFound(this ILogger logger, string pathPrefix);

    [LoggerMessage(
        EventId = 44,
        EventName = "SkippingPathNoMatch",
        Level = LogLevel.Trace,
        Message = "Skipping {Path}: Doesn't match pattern"
    )]
    public static partial void LogSkippingPathNoMatch(this ILogger logger, string path);
}
