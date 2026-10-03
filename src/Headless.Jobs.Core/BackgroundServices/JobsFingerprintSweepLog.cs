// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Headless.Jobs.BackgroundServices;

internal static partial class JobsFingerprintSweepLog
{
    [LoggerMessage(
        EventId = 3230,
        Level = LogLevel.Information,
        Message = "Cron fingerprint sweep completed: scanned={Scanned}, rebased={Rebased}, deferred={Deferred}, "
            + "lostFence={LostFence}."
    )]
    public static partial void SweepCompleted(ILogger logger, int scanned, int rebased, int deferred, int lostFence);

    [LoggerMessage(
        EventId = 3231,
        Level = LogLevel.Warning,
        Message = "Cron fingerprint sweep failed; the saved cursor will retry on the next interval."
    )]
    public static partial void SweepFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 3232,
        Level = LogLevel.Warning,
        Message = "Cron fingerprint sweep reached its per-pass bound after scanning {Count} definitions; continuation "
            + "state is retained for the next interval."
    )]
    public static partial void DrainBoundReached(ILogger logger, int count);
}
