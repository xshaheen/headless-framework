// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Linq.Expressions;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Models;
using Microsoft.EntityFrameworkCore.Query;

namespace Headless.Jobs.Infrastructure;

internal static class MappingExtensions
{
    public static Expression<Func<TCronJob, CronJobEntity>> ForCronJobExpressions<TCronJob>()
        where TCronJob : CronJobEntity, new() =>
        e => new CronJobEntity
        {
            Id = e.Id,
            Expression = e.Expression,
            Function = e.Function,
            RetryIntervals = e.RetryIntervals,
            Retries = e.Retries,
        };

    internal static Expression<Func<TTimeJob, TimeJobEntity>> ForQueueTimeJobs<TTimeJob>()
        where TTimeJob : TimeJobEntity<TTimeJob>, new() =>
        e => new TimeJobEntity
        {
            Id = e.Id,
            Function = e.Function,
            Retries = e.Retries,
            RetryIntervals = e.RetryIntervals,
            UpdatedAt = e.UpdatedAt,
            ParentId = e.ParentId,
            ExecutionTime = e.ExecutionTime,
            OnNodeDeath = e.OnNodeDeath,
            Children = e
                .Children.Select(ch => new TimeJobEntity
                {
                    Id = ch.Id,
                    Function = ch.Function,
                    Retries = ch.Retries,
                    RetryIntervals = ch.RetryIntervals,
                    RunCondition = ch.RunCondition,
                    OnNodeDeath = ch.OnNodeDeath,
                    Children = ch
                        .Children.Select(gch => new TimeJobEntity
                        {
                            Function = gch.Function,
                            Retries = gch.Retries,
                            RetryIntervals = gch.RetryIntervals,
                            Id = gch.Id,
                            RunCondition = gch.RunCondition,
                            OnNodeDeath = gch.OnNodeDeath,
                        })
                        .ToArray(),
                })
                .ToArray(),
        };

    internal static Expression<Func<TCronJobOccurrence, CronJobOccurrenceEntity<TCronJob>>> ForQueueCronJobOccurrence<
        TCronJobOccurrence,
        TCronJob
    >()
        where TCronJobOccurrence : CronJobOccurrenceEntity<TCronJob>, new()
        where TCronJob : CronJobEntity, new() =>
        e => new CronJobOccurrenceEntity<TCronJob>
        {
            Id = e.Id,
            UpdatedAt = e.UpdatedAt,
            CronJobId = e.CronJobId,
            OnNodeDeath = e.OnNodeDeath,
            CronJob = new TCronJob
            {
                Id = e.CronJob.Id,
                Function = e.CronJob.Function,
                RetryIntervals = e.CronJob.RetryIntervals,
                Retries = e.CronJob.Retries,
                OnNodeDeath = e.CronJob.OnNodeDeath,
            },
        };

    internal static Expression<
        Func<TCronJobOccurrence, CronJobOccurrenceEntity<TCronJob>>
    > ForLatestQueuedCronJobOccurrence<TCronJobOccurrence, TCronJob>()
        where TCronJobOccurrence : CronJobOccurrenceEntity<TCronJob>, new()
        where TCronJob : CronJobEntity, new() =>
        e => new CronJobOccurrenceEntity<TCronJob>
        {
            Id = e.Id,
            CreatedAt = e.CreatedAt,
            CronJobId = e.CronJobId,
            ExecutionTime = e.ExecutionTime,
            // Carry the stored death policy through the executor-pick projection (mirrors ForQueueCronJobOccurrence,
            // which stamps BOTH the occurrence-level and the nested CronJob.OnNodeDeath). The sole consumer,
            // InternalJobsManager._EarliestCronJobGroup, reads the NESTED `earliestStored.CronJob.OnNodeDeath`, so the
            // nested stamp below is the load-bearing one — without it a MarkFailed/Skip occurrence degrades to the
            // Retry enum default when re-queued.
            OnNodeDeath = e.OnNodeDeath,
            CronJob = new TCronJob
            {
                Id = e.CronJob.Id,
                Function = e.CronJob.Function,
                Expression = e.CronJob.Expression,
                RetryIntervals = e.CronJob.RetryIntervals,
                Retries = e.CronJob.Retries,
                OnNodeDeath = e.CronJob.OnNodeDeath,
            },
        };

    internal static void UpdateCronJobOccurrence<TCronJob>(
        this UpdateSettersBuilder<CronJobOccurrenceEntity<TCronJob>> setters,
        JobExecutionState functionContext
    )
        where TCronJob : CronJobEntity, new()
    {
        var propsToUpdate = functionContext.PropertiesToUpdate;

        // STATUS / SKIPPED
        if (propsToUpdate.Contains(nameof(JobExecutionState.Status)) && functionContext.Status != JobStatus.Skipped)
        {
            setters.SetProperty(x => x.Status, functionContext.Status);
        }
        else
        {
            setters
                .SetProperty(x => x.Status, functionContext.Status)
                .SetProperty(x => x.SkippedReason, functionContext.ExceptionDetails);
        }

        // EXECUTED_AT
        if (propsToUpdate.Contains(nameof(JobExecutionState.ExecutedAt)))
        {
            setters.SetProperty(x => x.ExecutedAt, functionContext.ExecutedAt);
        }

        // EXCEPTION DETAILS
        if (
            propsToUpdate.Contains(nameof(JobExecutionState.ExceptionDetails))
            && functionContext.Status != JobStatus.Skipped
        )
        {
            setters.SetProperty(x => x.ExceptionMessage, functionContext.ExceptionDetails);
        }

        // ELAPSED_TIME
        if (propsToUpdate.Contains(nameof(JobExecutionState.ElapsedTime)))
        {
            setters.SetProperty(x => x.ElapsedTime, functionContext.ElapsedTime);
        }

        // RETRY COUNT
        if (propsToUpdate.Contains(nameof(JobExecutionState.RetryCount)))
        {
            setters.SetProperty(x => x.RetryCount, functionContext.RetryCount);
        }

        // RELEASE LOCK
        if (propsToUpdate.Contains(nameof(JobExecutionState.ReleaseLock)))
        {
            setters.SetProperty(x => x.OwnerId, (string?)null).SetProperty(x => x.LockedUntil, (DateTime?)null);
        }

        // EXECUTION TIME
        if (propsToUpdate.Contains(nameof(JobExecutionState.ExecutionTime)))
        {
            setters.SetProperty(x => x.ExecutionTime, functionContext.ExecutionTime);
        }
    }

    internal static void UpdateTimeJob<TTimeJob>(
        this UpdateSettersBuilder<TTimeJob> setters,
        JobExecutionState functionContext,
        DateTime updatedAt
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
    {
        var propsToUpdate = functionContext.PropertiesToUpdate;

        // STATUS / SKIPPED
        if (propsToUpdate.Contains(nameof(JobExecutionState.Status)) && functionContext.Status != JobStatus.Skipped)
        {
            setters.SetProperty(x => x.Status, functionContext.Status);
        }
        else
        {
            setters
                .SetProperty(x => x.Status, functionContext.Status)
                .SetProperty(x => x.SkippedReason, functionContext.ExceptionDetails);
        }

        // EXECUTED_AT
        if (propsToUpdate.Contains(nameof(JobExecutionState.ExecutedAt)))
        {
            setters.SetProperty(x => x.ExecutedAt, functionContext.ExecutedAt);
        }

        // EXCEPTION DETAILS
        if (
            propsToUpdate.Contains(nameof(JobExecutionState.ExceptionDetails))
            && functionContext.Status != JobStatus.Skipped
        )
        {
            setters.SetProperty(x => x.ExceptionMessage, functionContext.ExceptionDetails);
        }

        // ELAPSED_TIME
        if (propsToUpdate.Contains(nameof(JobExecutionState.ElapsedTime)))
        {
            setters.SetProperty(x => x.ElapsedTime, functionContext.ElapsedTime);
        }

        // RETRY COUNT
        if (propsToUpdate.Contains(nameof(JobExecutionState.RetryCount)))
        {
            setters.SetProperty(x => x.RetryCount, functionContext.RetryCount);
        }

        // RELEASE LOCK
        if (propsToUpdate.Contains(nameof(JobExecutionState.ReleaseLock)))
        {
            setters.SetProperty(x => x.OwnerId, (string?)null).SetProperty(x => x.LockedUntil, (DateTime?)null);
        }

        // UPDATED_AT ALWAYS
        setters.SetProperty(x => x.UpdatedAt, updatedAt);
    }
}
