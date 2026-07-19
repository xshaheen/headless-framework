// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Entities;
using Headless.Jobs.Enums;

namespace Headless.Jobs.Managers;

internal static class CronJobOccurrenceFactory
{
    public static CronJobOccurrenceEntity<TCronJob> Create<TCronJob>(
        TCronJob definition,
        DateTime executionTime,
        DateTime now
    )
        where TCronJob : CronJobEntity
    {
        return new CronJobOccurrenceEntity<TCronJob>
        {
            Id = Guid.NewGuid(),
            CronJobId = definition.Id,
            CronJob = definition,
            ExecutionTime = executionTime,
            Status = JobStatus.Idle,
            OnNodeDeath = definition.OnNodeDeath,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }
}
