// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

namespace Headless.Tus.Services;

internal static partial class TusAzureLoggerExtensions
{
    [LoggerMessage(
        EventId = 3204,
        EventName = "CreatedPartialFile",
        Level = LogLevel.Debug,
        Message = "Created partial file {FileId} with upload length {UploadLength}"
    )]
    public static partial void LogCreatedPartialFile(this ILogger logger, string fileId, long uploadLength);

    [LoggerMessage(
        EventId = 3205,
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
        EventId = 3241,
        EventName = "StageBlockFromUriNotSupported",
        Level = LogLevel.Warning,
        Message = "Server-side StageBlockFromUri unavailable (HTTP {Status}); falling back to streaming copy"
    )]
    public static partial void LogStageBlockFromUriNotSupported(this ILogger logger, int status);

    [LoggerMessage(
        EventId = 3242,
        EventName = "CreatedFinalFile",
        Level = LogLevel.Debug,
        Message = "Created final file {FileId} from {PartCount} partial file(s), total size: {TotalSize}"
    )]
    public static partial void LogCreatedFinalFile(this ILogger logger, string fileId, int partCount, long totalSize);

    [LoggerMessage(
        EventId = 3243,
        EventName = "FailedToCreateFinalFile",
        Level = LogLevel.Error,
        Message = "Failed to create final file from partial files: {PartialFiles}"
    )]
    public static partial void LogFailedToCreateFinalFile(
        this ILogger logger,
        Exception exception,
        string partialFiles
    );

    [LoggerMessage(
        EventId = 3250,
        EventName = "FailedToDeletePartialFileAfterConcat",
        Level = LogLevel.Warning,
        Message = "Failed to delete partial file {PartialFileId} after concatenation; the final upload is unaffected"
    )]
    public static partial void LogFailedToDeletePartialFileAfterConcat(
        this ILogger logger,
        Exception exception,
        string partialFileId
    );
}
