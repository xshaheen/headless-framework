// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Fencing;

namespace Headless.Jobs;

/// <summary>
/// The logical table names a Jobs <see cref="FencedRow{TKey}"/> carries. They name which kind of row a claim holds,
/// not where it is stored: a persistence provider maps each one to its own table before it fences a write.
/// </summary>
[PublicAPI]
public static class JobsLeaseTables
{
    /// <summary>The logical table of time job rows.</summary>
    public const string TimeJobs = "jobs.time_jobs";

    /// <summary>The logical table of cron occurrence rows.</summary>
    public const string CronOccurrences = "jobs.cron_job_occurrences";

    /// <summary>Returns the logical table that holds rows of <paramref name="type"/>.</summary>
    public static string For(JobType type) => type == JobType.CronJobOccurrence ? CronOccurrences : TimeJobs;

    /// <summary>Returns the fenced row a time job's current lease holds.</summary>
    public static FencedRow<Guid> TimeJob(Guid id, long generation) => new(TimeJobs, id, generation);

    /// <summary>Returns the fenced row a cron occurrence's current lease holds.</summary>
    public static FencedRow<Guid> CronOccurrence(Guid id, long generation) => new(CronOccurrences, id, generation);
}
