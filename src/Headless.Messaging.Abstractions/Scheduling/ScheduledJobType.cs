// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Defines the scheduling type for a job.
/// </summary>
public enum ScheduledJobType
{
    /// <summary>
    /// A recurring job that runs on a cron schedule.
    /// </summary>
    Recurring,

    /// <summary>
    /// A one-time job that runs once at a specific time.
    /// </summary>
    OneTime,
}
