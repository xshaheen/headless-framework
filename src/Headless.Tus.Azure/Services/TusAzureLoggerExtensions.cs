// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

namespace Headless.Tus.Services;

internal static partial class TusAzureLoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        EventName = "CreatedPartialFile",
        Level = LogLevel.Debug,
        Message = "Created partial file {FileId} with upload length {UploadLength}"
    )]
    public static partial void LogCreatedPartialFile(this ILogger logger, string fileId, long uploadLength);

    [LoggerMessage(
        EventId = 2,
        EventName = "FailedToCreatePartialFile",
        Level = LogLevel.Error,
        Message = "Failed to create partial file with upload length {UploadLength}"
    )]
    public static partial void LogFailedToCreatePartialFile(
        this ILogger logger,
        Exception exception,
        long uploadLength
    );

    [LoggerMessage(
        EventId = 3,
        EventName = "StageBlockFromUriNotSupported",
        Level = LogLevel.Warning,
        Message = "StageBlockFromUri not supported, falling back to streaming"
    )]
    public static partial void LogStageBlockFromUriNotSupported(this ILogger logger);

    [LoggerMessage(
        EventId = 4,
        EventName = "CreatedFinalFile",
        Level = LogLevel.Debug,
        Message = "Created final file {FileId} from {PartCount} partial file(s), total size: {TotalSize}"
    )]
    public static partial void LogCreatedFinalFile(this ILogger logger, string fileId, int partCount, long totalSize);

    [LoggerMessage(
        EventId = 5,
        EventName = "FailedToCreateFinalFile",
        Level = LogLevel.Error,
        Message = "Failed to create final file from partial files: {PartialFiles}"
    )]
    public static partial void LogFailedToCreateFinalFile(
        this ILogger logger,
        Exception exception,
        string partialFiles
    );
}
