// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.Models;
using Microsoft.Extensions.Logging;

namespace Headless.Jobs.BackgroundServices;

// Shared by JobsSchedulerBackgroundService and JobsFallbackBackgroundService — the per-service ILogger
// instance keeps the log category distinct while the message shape stays in one place.
internal static partial class JobsAdmissionClaimLog
{
    [LoggerMessage(
        EventId = 3210,
        Level = LogLevel.Warning,
        Message = "Admission-time claim write for job {JobId} ({FunctionName}) failed; the row stays Queued until the fallback sweep re-claims it after its lease lapses."
    )]
    public static partial void LogJobAdmissionClaimFailed(
        this ILogger logger,
        Exception exception,
        Guid jobId,
        string functionName
    );

    [LoggerMessage(
        EventId = 3211,
        Level = LogLevel.Debug,
        Message = "Admission-time claim for job {JobId} ({FunctionName}) affected no rows (ownership lapsed or another wrapper won); skipping execution."
    )]
    public static partial void LogJobAdmissionClaimLost(this ILogger logger, Guid jobId, string functionName);
}
