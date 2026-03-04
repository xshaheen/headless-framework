// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Represents the current status of a scheduled job.
/// </summary>
public enum ScheduledJobStatus
{
    /// <summary>
    /// The job is registered but not yet running.
    /// </summary>
    Pending,

    /// <summary>
    /// The job is currently executing.
    /// </summary>
    Running,

    /// <summary>
    /// The job completed its last execution successfully.
    /// </summary>
    Completed,

    /// <summary>
    /// The job's last execution failed.
    /// </summary>
    Failed,

    /// <summary>
    /// The job has been disabled and will not execute.
    /// </summary>
    Disabled,
}
