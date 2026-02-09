// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Represents the status of a single job execution attempt.
/// </summary>
public enum JobExecutionStatus
{
    /// <summary>
    /// The execution is waiting to start.
    /// </summary>
    Pending,

    /// <summary>
    /// The execution is currently in progress.
    /// </summary>
    Running,

    /// <summary>
    /// The execution completed successfully.
    /// </summary>
    Succeeded,

    /// <summary>
    /// The execution failed.
    /// </summary>
    Failed,

    /// <summary>
    /// The execution was terminated by stale job recovery because the owning process
    /// became unresponsive or crashed before completing the job.
    /// </summary>
    TimedOut,

}
