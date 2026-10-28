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
}
