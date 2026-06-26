// Copyright (c) Mahmoud Shaheen. All rights reserved.

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
        EventName = "CreatingContainerSegment",
        Level = LogLevel.Trace,
        Message = "Creating directory segment {Segment}"
    )]
    public static partial void LogCreatingContainerSegment(this ILogger logger, string segment);

    [LoggerMessage(EventId = 14, EventName = "SavingBlob", Level = LogLevel.Trace, Message = "Saving {Path}")]
    public static partial void LogSavingBlob(this ILogger logger, string path);

    [LoggerMessage(
        EventId = 15,
        EventName = "ErrorSavingBlobCreatingDirectory",
        Level = LogLevel.Debug,
        Message = "Error saving {Path}: Attempting to create directory"
    )]
    public static partial void LogErrorSavingBlobCreatingDirectory(
        this ILogger logger,
        Exception exception,
        string path
    );

    [LoggerMessage(EventId = 16, EventName = "DeletingBlob", Level = LogLevel.Trace, Message = "Deleting {Path}")]
    public static partial void LogDeletingBlob(this ILogger logger, string path);

    [LoggerMessage(
        EventId = 17,
        EventName = "DeletingAllByPrefix",
        Level = LogLevel.Trace,
        Message = "Deleting all blobs under prefix {Prefix}"
    )]
    public static partial void LogDeletingAllByPrefix(this ILogger logger, string? prefix);

    [LoggerMessage(
        EventId = 18,
        EventName = "CopyingBlob",
        Level = LogLevel.Trace,
        Message = "Copying {Source} to {Destination}"
    )]
    public static partial void LogCopyingBlob(this ILogger logger, string source, string destination);

    [LoggerMessage(
        EventId = 19,
        EventName = "CopySourceNotFound",
        Level = LogLevel.Debug,
        Message = "Copy source not found: {Source}"
    )]
    public static partial void LogCopySourceNotFound(this ILogger logger, Exception exception, string source);

    [LoggerMessage(
        EventId = 20,
        EventName = "MoveRollback",
        Level = LogLevel.Error,
        Message = "Failed to delete source {Source} after copy to {Destination}; rolling back the destination copy"
    )]
    public static partial void LogMoveRollback(
        this ILogger logger,
        Exception exception,
        string source,
        string destination
    );

    [LoggerMessage(
        EventId = 21,
        EventName = "MoveRollbackFailed",
        Level = LogLevel.Error,
        Message = "Failed to roll back the destination copy {Destination} after a failed move"
    )]
    public static partial void LogMoveRollbackFailed(this ILogger logger, Exception exception, string destination);

    [LoggerMessage(
        EventId = 22,
        EventName = "CheckingBlobExists",
        Level = LogLevel.Trace,
        Message = "Checking if {Path} exists"
    )]
    public static partial void LogCheckingBlobExists(this ILogger logger, string path);

    [LoggerMessage(
        EventId = 23,
        EventName = "GettingFileStream",
        Level = LogLevel.Trace,
        Message = "Getting file stream for {Path}"
    )]
    public static partial void LogGettingFileStream(this ILogger logger, string path);

    [LoggerMessage(
        EventId = 24,
        EventName = "FileStreamNotFound",
        Level = LogLevel.Debug,
        Message = "Unable to get file stream for {Path}: File not found"
    )]
    public static partial void LogFileStreamNotFound(this ILogger logger, Exception exception, string path);

    [LoggerMessage(
        EventId = 25,
        EventName = "GettingBlobInfo",
        Level = LogLevel.Trace,
        Message = "Getting blob info for {Path}"
    )]
    public static partial void LogGettingBlobInfo(this ILogger logger, string path);

    [LoggerMessage(
        EventId = 26,
        EventName = "BlobInfoIsDirectory",
        Level = LogLevel.Debug,
        Message = "Unable to get blob info for {Path}: Is a directory"
    )]
    public static partial void LogBlobInfoIsDirectory(this ILogger logger, string path);

    [LoggerMessage(
        EventId = 27,
        EventName = "BlobInfoNotFound",
        Level = LogLevel.Debug,
        Message = "Unable to get blob info for {Path}: File not found"
    )]
    public static partial void LogBlobInfoNotFound(this ILogger logger, Exception exception, string path);

    [LoggerMessage(
        EventId = 28,
        EventName = "CancellationRequested",
        Level = LogLevel.Debug,
        Message = "Cancellation requested"
    )]
    public static partial void LogCancellationRequested(this ILogger logger);

    [LoggerMessage(
        EventId = 29,
        EventName = "DirectoryNotFound",
        Level = LogLevel.Debug,
        Message = "Directory not found: {Directory}"
    )]
    public static partial void LogDirectoryNotFound(this ILogger logger, string directory);

    // === SshBlobContainerManager ===

    [LoggerMessage(
        EventId = 30,
        EventName = "EnsuringContainer",
        Level = LogLevel.Trace,
        Message = "Ensuring container {Container} exists"
    )]
    public static partial void LogEnsuringContainer(this ILogger logger, string container);

    [LoggerMessage(
        EventId = 31,
        EventName = "DeletingContainer",
        Level = LogLevel.Information,
        Message = "Deleting container {Container}"
    )]
    public static partial void LogDeletingContainer(this ILogger logger, string container);

    [LoggerMessage(
        EventId = 32,
        EventName = "DeleteContainerNotFound",
        Level = LogLevel.Debug,
        Message = "Container {Container} not found for deletion"
    )]
    public static partial void LogDeleteContainerNotFound(this ILogger logger, string container);
}
