// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;

namespace Headless.Jobs.Managers;

internal static class CronJobOccurrenceFactory
{
    /// <summary>
    /// The occurrence factory every fenced schedule write hands to its provider. The provider stamps the store clock
    /// inside its own transaction and calls this back with that exact persisted anchor, so the replacement occurrence
    /// is derived from the instant that was durably recorded rather than from a node-local guess. Returns
    /// <see langword="null" /> when the expression has no occurrence after the anchor, which the provider treats as
    /// "no replacement".
    /// </summary>
    public static Func<DateTime, CronJobOccurrenceEntity<TCronJob>?> CreateStoreAnchored<TCronJob>(
        TCronJob definition,
        CronScheduleCache cronScheduleCache,
        DateTimeOffset now,
        IGuidGenerator guidGenerator
    )
        where TCronJob : CronJobEntity
    {
        Argument.IsNotNull(definition);
        Argument.IsNotNull(cronScheduleCache);
        Argument.IsNotNull(guidGenerator);

        return scheduleAnchorUtc =>
        {
            var storeAnchoredNext = cronScheduleCache.GetNextOccurrenceOrDefault(
                definition.Expression,
                scheduleAnchorUtc,
                definition.TimeZoneId
            );

            return storeAnchoredNext is null ? null : Create(definition, storeAnchoredNext.Value, now, guidGenerator);
        };
    }

    public static CronJobOccurrenceEntity<TCronJob> Create<TCronJob>(
        TCronJob definition,
        DateTime executionTime,
        DateTimeOffset now,
        IGuidGenerator guidGenerator
    )
        where TCronJob : CronJobEntity
    {
        var occurrence = new CronJobOccurrenceEntity<TCronJob>
        {
            Id = guidGenerator.Create(),
            CronJobId = definition.Id,
            CronJob = definition,
            ExecutionTime = executionTime,
            Status = JobStatus.Idle,
            OnNodeDeath = definition.OnNodeDeath,
            CreatedAt = now,
            UpdatedAt = now,
        };
        occurrence.SnapshotContract(definition);
        return occurrence;
    }
}
