// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Scheduling;

/// <summary>
/// Configuration options for the scheduler background service.
/// </summary>
public sealed class SchedulerOptions
{
    /// <summary>
    /// Gets or sets the polling interval between scheduler runs. Default: 1 second.
    /// </summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets or sets the maximum number of jobs to acquire per poll cycle. Default: 10.
    /// </summary>
    public int BatchSize { get; set; } = 10;

    /// <summary>
    /// Gets or sets the lock holder identifier used when acquiring jobs.
    /// Default: <see cref="Environment.MachineName"/>.
    /// </summary>
    public string LockHolder { get; set; } = Environment.MachineName;

    /// <summary>
    /// Gets or sets the distributed lock timeout for job execution when using
    /// an <c>IDistributedLockProvider</c> with <c>SkipIfRunning</c> jobs. Default: 5 minutes.
    /// </summary>
    public TimeSpan LockTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets or sets the threshold for determining if a scheduled execution is considered misfired.
    /// A job is misfired if its scheduled time is more than this duration in the past. Default: 1 minute.
    /// </summary>
    public TimeSpan MisfireThreshold { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Gets or sets the threshold for determining if a job is stale (locked but not progressing).
    /// Jobs locked longer than this duration are released back to Pending status. Default: 5 minutes.
    /// </summary>
    public TimeSpan StaleJobThreshold { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets or sets the polling interval for the stale job recovery service. Default: 30 seconds.
    /// </summary>
    public TimeSpan StaleJobCheckInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the default timeout for job execution when a job does not specify its own timeout.
    /// If both this property and the job's timeout are null, no timeout is enforced. Default: null (no timeout).
    /// </summary>
    public TimeSpan? DefaultJobTimeout { get; set; }

    /// <summary>
    /// Gets or sets how long completed execution records are retained before being purged.
    /// The stale job recovery service periodically deletes execution records older than this duration.
    /// Default: 7 days.
    /// </summary>
    public TimeSpan ExecutionRetention { get; set; } = TimeSpan.FromDays(7);
}
