// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Linq.Expressions;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace Headless.Jobs.Infrastructure;

internal static class MappingExtensions
{
    internal static TCronJob ProjectCronJob<TCronJob>(JobManagerDispatchContext item, string owner)
        where TCronJob : CronJobEntity, new()
    {
        return new()
        {
            Id = item.Id,
            Function = item.FunctionName,
            InitIdentifier = owner,
            Expression = item.Expression,
            TimeZoneId = item.TimeZoneId,
            IsPaused = item.IsPaused,
            ScheduleRevision = item.ScheduleRevision,
            Retries = item.Retries,
            RetryIntervals = item.RetryIntervals,
        };
    }

    public static Expression<Func<TCronJob, CronJobEntity>> ForCronJobExpressions<TCronJob>()
        where TCronJob : CronJobEntity, new()
    {
        return e => new CronJobEntity
        {
            Id = e.Id,
            Expression = e.Expression,
            Function = e.Function,
            TimeZoneId = e.TimeZoneId,
            IsPaused = e.IsPaused,
            ScheduleRevision = e.ScheduleRevision,
            CreatedAt = e.CreatedAt,
            UpdatedAt = e.UpdatedAt,
            RetryIntervals = e.RetryIntervals,
            Retries = e.Retries,
        };
    }

    internal static Expression<Func<TTimeJob, TimeJobEntity>> ForQueueTimeJobs<TTimeJob>()
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
    {
        return e => new TimeJobEntity
        {
            Id = e.Id,
            Function = e.Function,
            Retries = e.Retries,
            RetryCount = e.RetryCount,
            RetryIntervals = e.RetryIntervals,
            CreatedAt = e.CreatedAt,
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
                    RetryCount = ch.RetryCount,
                    RetryIntervals = ch.RetryIntervals,
                    CreatedAt = ch.CreatedAt,
                    UpdatedAt = ch.UpdatedAt,
                    ParentId = ch.ParentId,
                    RunCondition = ch.RunCondition,
                    OnNodeDeath = ch.OnNodeDeath,
                    Children = ch
                        .Children.Select(gch => new TimeJobEntity
                        {
                            Function = gch.Function,
                            Retries = gch.Retries,
                            RetryCount = gch.RetryCount,
                            RetryIntervals = gch.RetryIntervals,
                            Id = gch.Id,
                            CreatedAt = gch.CreatedAt,
                            UpdatedAt = gch.UpdatedAt,
                            ParentId = gch.ParentId,
                            RunCondition = gch.RunCondition,
                            OnNodeDeath = gch.OnNodeDeath,
                        })
                        .ToArray(),
                })
                .ToArray(),
        };
    }

    // KTD2: a single node projected flat (no nested children). A recursive .Select projection is not EF-translatable,
    // so deep hydration claims the id-set to depth, reloads these flat rows, and rebuilds the tree by ParentId in
    // memory (AttachNonTimedDescendantsAsync). Carries the full ForQueueTimeJobs field set — dropping RetryCount (or
    // any field) from any pickup path silently resets state after restart (docs/solutions precedent).
    internal static Expression<Func<TTimeJob, TimeJobEntity>> ForFlatTimeJob<TTimeJob>()
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
    {
        return e => new TimeJobEntity
        {
            Id = e.Id,
            Function = e.Function,
            Retries = e.Retries,
            RetryCount = e.RetryCount,
            RetryIntervals = e.RetryIntervals,
            CreatedAt = e.CreatedAt,
            UpdatedAt = e.UpdatedAt,
            ParentId = e.ParentId,
            ExecutionTime = e.ExecutionTime,
            RunCondition = e.RunCondition,
            OnNodeDeath = e.OnNodeDeath,
        };
    }

    /// <summary>
    /// R12/KTD2: attaches the non-timed in-tree subtree to each already-loaded flat root, frontier by frontier, down
    /// to <paramref name="maxChainDepth"/> (roots are depth 1). A timed descendant (<c>ExecutionTime != null</c>) is a
    /// boundary — excluded from the in-tree walk and claimed independently (U5) — so the frontier descends only
    /// through non-timed children. The tree is rebuilt by <c>ParentId</c> in memory because a recursive EF projection
    /// is not translatable.
    /// </summary>
    internal static async Task AttachNonTimedDescendantsAsync<TTimeJob>(
        IQueryable<TTimeJob> source,
        IReadOnlyCollection<TimeJobEntity> roots,
        int maxChainDepth,
        CancellationToken cancellationToken
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
    {
        if (roots.Count == 0)
        {
            return;
        }

        var childrenByParent = new Dictionary<Guid, List<TimeJobEntity>>();
        var frontier = roots.Select(x => x.Id).ToArray();
        var depth = 1;

        while (frontier.Length != 0 && depth < maxChainDepth)
        {
            var parentIds = frontier;

            var children = await source
                .Where(x =>
                    x.ParentId != null
                    && ((IEnumerable<Guid>)parentIds).Contains(x.ParentId.Value)
                    && x.ExecutionTime == null
                )
                .Select(ForFlatTimeJob<TTimeJob>())
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            if (children.Length == 0)
            {
                break;
            }

            foreach (var child in children)
            {
                if (child.ParentId is not { } parentId)
                {
                    continue;
                }

                if (!childrenByParent.TryGetValue(parentId, out var bucket))
                {
                    bucket = [];
                    childrenByParent[parentId] = bucket;
                }

                bucket.Add(child);
            }

            frontier = children.Select(x => x.Id).ToArray();
            depth++;
        }

        foreach (var root in roots)
        {
            _AttachChildren(root, childrenByParent);
        }
    }

    private static void _AttachChildren(TimeJobEntity node, Dictionary<Guid, List<TimeJobEntity>> childrenByParent)
    {
        if (!childrenByParent.TryGetValue(node.Id, out var children))
        {
            return;
        }

        node.Children = children;

        foreach (var child in children)
        {
            _AttachChildren(child, childrenByParent);
        }
    }

    /// <summary>
    /// KTD2: keeps only the descendants the claim actually leased. The claimed set is prefix-closed (a node is claimed
    /// only after its parent chain was), so pruning the hydrated tree to it yields exactly the executable subtree — a
    /// node below a non-idle frontier the claim stopped at (terminalized/running) is dropped rather than executed
    /// unclaimed.
    /// </summary>
    internal static void PruneToClaimedSet(TimeJobEntity node, HashSet<Guid> claimedIds)
    {
        var kept = new List<TimeJobEntity>(node.Children.Count);

        foreach (var child in node.Children)
        {
            if (!claimedIds.Contains(child.Id))
            {
                continue;
            }

            PruneToClaimedSet(child, claimedIds);
            kept.Add(child);
        }

        node.Children = kept;
    }

    internal static Expression<Func<TCronJobOccurrence, CronJobOccurrenceEntity<TCronJob>>> ForQueueCronJobOccurrence<
        TCronJobOccurrence,
        TCronJob
    >()
        where TCronJobOccurrence : CronJobOccurrenceEntity<TCronJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        return e => new CronJobOccurrenceEntity<TCronJob>
        {
            Id = e.Id,
            CreatedAt = e.CreatedAt,
            UpdatedAt = e.UpdatedAt,
            CronJobId = e.CronJobId,
            RetryCount = e.RetryCount,
            ExecutionTime = e.ExecutionTime,
            OnNodeDeath = e.OnNodeDeath,
            CronJob = new TCronJob
            {
                Id = e.CronJob.Id,
                Function = e.CronJob.Function,
                CreatedAt = e.CronJob.CreatedAt,
                UpdatedAt = e.CronJob.UpdatedAt,
                RetryIntervals = e.CronJob.RetryIntervals,
                Retries = e.CronJob.Retries,
                OnNodeDeath = e.CronJob.OnNodeDeath,
            },
        };
    }

    internal static Expression<
        Func<TCronJobOccurrence, CronJobOccurrenceEntity<TCronJob>>
    > ForLatestQueuedCronJobOccurrence<TCronJobOccurrence, TCronJob>()
        where TCronJobOccurrence : CronJobOccurrenceEntity<TCronJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        return e => new CronJobOccurrenceEntity<TCronJob>
        {
            Id = e.Id,
            CreatedAt = e.CreatedAt,
            UpdatedAt = e.UpdatedAt,
            CronJobId = e.CronJobId,
            ExecutionTime = e.ExecutionTime,
            // Carry the stored death policy through the executor-pick projection (mirrors ForQueueCronJobOccurrence,
            // which stamps BOTH the occurrence-level and the nested CronJob.OnNodeDeath). The sole consumer,
            // InternalJobsManager._EarliestCronJobGroup, reads the NESTED `earliestStored.CronJob.OnNodeDeath`, so the
            // nested stamp below is the load-bearing one — without it a MarkFailed/Skip occurrence degrades to the
            // Retry enum default when re-queued.
            OnNodeDeath = e.OnNodeDeath,
            RetryCount = e.RetryCount,
            CronJob = new TCronJob
            {
                Id = e.CronJob.Id,
                Function = e.CronJob.Function,
                CreatedAt = e.CronJob.CreatedAt,
                UpdatedAt = e.CronJob.UpdatedAt,
                Expression = e.CronJob.Expression,
                RetryIntervals = e.CronJob.RetryIntervals,
                Retries = e.CronJob.Retries,
                OnNodeDeath = e.CronJob.OnNodeDeath,
            },
        };
    }

    internal static void UpdateCronJobOccurrence<TCronJob>(
        this UpdateSettersBuilder<CronJobOccurrenceEntity<TCronJob>> setters,
        JobExecutionState functionContext
    )
        where TCronJob : CronJobEntity, new()
    {
        var propsToUpdate = functionContext.PropertiesToUpdate;

        // STATUS / SKIPPED
        if (propsToUpdate.Contains(nameof(JobExecutionState.Status)))
        {
            if (functionContext.Status == JobStatus.Skipped)
            {
                setters
                    .SetProperty(x => x.Status, functionContext.Status)
                    .SetProperty(x => x.SkippedReason, functionContext.ExceptionDetails);
            }
            else
            {
                setters.SetProperty(x => x.Status, functionContext.Status);
            }
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
        if (propsToUpdate.Contains(nameof(JobExecutionState.Status)))
        {
            if (functionContext.Status == JobStatus.Skipped)
            {
                setters
                    .SetProperty(x => x.Status, functionContext.Status)
                    .SetProperty(x => x.SkippedReason, functionContext.ExceptionDetails);
            }
            else
            {
                setters.SetProperty(x => x.Status, functionContext.Status);
            }
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
