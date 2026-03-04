// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Defines strategies for handling missed scheduled job executions.
/// </summary>
/// <remarks>
/// A misfire occurs when a job's scheduled execution time has passed
/// but the job has not yet been dispatched for execution.
/// </remarks>
public enum MisfireStrategy
{
    /// <summary>
    /// Fire the job immediately when discovered as misfired.
    /// </summary>
    /// <remarks>
    /// This is the default strategy. When a job misfire is detected,
    /// the job executes immediately to catch up.
    /// </remarks>
    FireImmediately = 0,

    /// <summary>
    /// Skip the misfired occurrence and schedule the next execution.
    /// </summary>
    /// <remarks>
    /// When a job misfire is detected, the missed occurrence is skipped
    /// and the job is rescheduled for its next occurrence based on the cron expression.
    /// </remarks>
    SkipAndScheduleNext = 1,
}
