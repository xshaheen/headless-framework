// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs.Enums;

/// <summary>
/// Discriminates the two schedulable row types that flow through the Jobs execution pipeline.
/// </summary>
/// <remarks>
/// New members may be added in future versions; consumers that <see langword="switch"/> on this enum
/// should include a default arm.
/// </remarks>
[PublicAPI]
public enum JobType
{
    /// <summary>A materialized occurrence row produced by a recurring cron job definition.</summary>
    CronJobOccurrence = 0,

    /// <summary>A one-shot delayed or immediate job scheduled via <c>ITimeJobManager</c>.</summary>
    TimeJob = 1,
}
