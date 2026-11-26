// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs;

/// <summary>Claims due work as Queued, so only worker admission starts the execution lease.</summary>
internal interface IJobImmediateAdmission<TCronJob>
    where TCronJob : CronJobEntity, new()
{
    Task<TimeJobEntity[]> QueueImmediateTimeJobsAsync(Guid[] ids, CancellationToken cancellationToken);

    Task<CronJobOccurrenceEntity<TCronJob>[]> QueueImmediateCronOccurrencesAsync(
        Guid[] occurrenceIds,
        CancellationToken cancellationToken
    );
}
