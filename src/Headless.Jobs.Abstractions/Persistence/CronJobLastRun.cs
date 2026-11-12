// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs;

/// <summary>A cron job's last run: its latest occurrence that started or ended.</summary>
/// <remarks>This record grows by <see langword="init"/> members.</remarks>
[PublicAPI]
public sealed record CronJobLastRun
{
    /// <summary>Gets the cron job.</summary>
    public required Guid CronJobId { get; init; }

    /// <summary>Gets the occurrence, which is the run's identifier.</summary>
    public required Guid OccurrenceId { get; init; }

    /// <summary>Gets the occurrence's status, never <see cref="JobStatus.Idle"/> or <see cref="JobStatus.Queued"/>.</summary>
    public required JobStatus Status { get; init; }

    /// <summary>
    /// Gets when the occurrence's last execution ended, or <see langword="null"/> while it is in progress.
    /// </summary>
    public DateTimeOffset? ExecutedAt { get; init; }
}
