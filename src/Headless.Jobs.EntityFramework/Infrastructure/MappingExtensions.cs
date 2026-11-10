// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Linq.Expressions;
using Headless.Fencing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

#pragma warning disable MA0133 // EF must keep DateTime.UtcNow in expression trees so providers translate the database clock before the DateTimeOffset assignment.
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
            ContractVersion = e.ContractVersion,
            CorrelationId = e.CorrelationId,
            CausationId = e.CausationId,
            TimeZoneId = e.TimeZoneId,
            IsPaused = e.IsPaused,
            ScheduleRevision = e.ScheduleRevision,
            CreatedAt = e.CreatedAt,
            UpdatedAt = e.UpdatedAt,
            RetryIntervals = e.RetryIntervals,
            Retries = e.Retries,
        };
    }

    // A single node projected flat (no nested children). A recursive .Select projection is not EF-translatable,
    // so deep hydration claims the id-set to depth, reloads these flat rows, and rebuilds the tree by ParentId in
    // memory (AttachNonTimedDescendantsAsync). Carries the full pickup field set — dropping RetryCount (or any field)
    // from any pickup path silently resets state after restart, a regression that shipped once.
    internal static Expression<Func<TTimeJob, TimeJobEntity>> ForFlatTimeJob<TTimeJob>()
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
    {
        return e => new TimeJobEntity
        {
            Id = e.Id,
            Function = e.Function,
            ContractVersion = e.ContractVersion,
            CorrelationId = e.CorrelationId,
            CausationId = e.CausationId,
            TraceParent = e.TraceParent,
            Retries = e.Retries,
            RetryCount = e.RetryCount,
            TenantId = e.TenantId,
            RetryIntervals = e.RetryIntervals,
            CreatedAt = e.CreatedAt,
            UpdatedAt = e.UpdatedAt,
            ParentId = e.ParentId,
            ExecutionTime = e.ExecutionTime,
            RunCondition = e.RunCondition,
            OnNodeDeath = e.OnNodeDeath,
            // The run fences its settling writes on the generation its claim granted each node, so the lease must
            // survive the reload, children included.
            Lease = e.Lease,
        };
    }

    /// <summary>
    /// Attaches the non-timed in-tree subtree to each already-loaded flat root, frontier by frontier, down
    /// to <paramref name="maxChainDepth"/> (roots are depth 1). A timed descendant (<c>ExecutionTime != null</c>) is a
    /// boundary — excluded from the in-tree walk and claimed independently — so the frontier descends only
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

        for (var depth = 1; frontier.Length != 0 && depth < maxChainDepth; depth++)
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

            frontier = [.. children.Select(x => x.Id)];
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
            Function = e.Function,
            ContractVersion = e.ContractVersion,
            CorrelationId = e.CorrelationId,
            CausationId = e.CausationId,
            TraceParent = e.TraceParent,
            TenantId = e.TenantId,
            Request = e.Request,
            RetryCount = e.RetryCount,
            // The recovery stamp rides every pickup/claim projection, so a coalesced run reclaimed after a
            // restart still reports the instant it stands for. Dropping it here silently demotes it to an ordinary
            // run — the RetryCount defect shape this repo has already paid for once.
            RecoveredFromUtc = e.RecoveredFromUtc,
            ExecutionTime = e.ExecutionTime,
            OnNodeDeath = e.OnNodeDeath,
            // The native claims return this projection, and the run fences its settling writes on the lease the
            // claim granted, so the generation and holder must survive it.
            Lease = e.Lease,
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
            Function = e.Function,
            ContractVersion = e.ContractVersion,
            CorrelationId = e.CorrelationId,
            CausationId = e.CausationId,
            TraceParent = e.TraceParent,
            TenantId = e.TenantId,
            // No payload: the scheduler builds a dispatch context from this row, and the run reads its request itself.
            ExecutionTime = e.ExecutionTime,
            // Carry the stored death policy through the executor-pick projection (mirrors ForQueueCronJobOccurrence,
            // which stamps BOTH the occurrence-level and the nested CronJob.OnNodeDeath). The sole consumer,
            // InternalJobsManager._EarliestCronJobGroup, reads the NESTED `earliestStored.CronJob.OnNodeDeath`, so the
            // nested stamp below is the load-bearing one — without it a MarkFailed/Skip occurrence degrades to the
            // Retry enum default when re-queued.
            OnNodeDeath = e.OnNodeDeath,
            RetryCount = e.RetryCount,
            // See the sibling projection — the recovery stamp must survive this read too.
            RecoveredFromUtc = e.RecoveredFromUtc,
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

    /// <summary>
    /// Writes an advance's schedule position, and its evaluation fingerprint when it carries one, so every path that
    /// moves a projection records the rules that produced it in the same statement.
    /// </summary>
    internal static void SetCronSchedulePosition<TCronJob>(
        this UpdateSettersBuilder<TCronJob> setters,
        CronScheduleAdvance advance
    )
        where TCronJob : CronJobEntity
    {
        // Plain locals, so the setter expressions capture DateTime and string values rather than property accesses on
        // the record that EF would have to translate.
        var reconciledThroughUtc = advance.ReconciledThroughUtc;
        var nextDueUtc = advance.NextDueUtc;
        setters
            .SetProperty(x => x.ReconciledThroughUtc, reconciledThroughUtc)
            .SetProperty(x => x.NextDueUtc, nextDueUtc)
            // The next due time is what a schedule list shows, so moving it is a change.
            .SetChangedAt();

        // Null leaves the persisted fingerprint alone.
        if (advance.EvaluationFingerprint is { } fingerprint)
        {
            setters.SetCronEvaluationFingerprint(fingerprint);
        }
    }

    /// <summary>
    /// Records the rules a new projection was derived under and clears any defer state the old rules caused, since a
    /// definition that evaluates under current rules is no longer stale.
    /// </summary>
    internal static void SetCronEvaluationFingerprint<TCronJob>(
        this UpdateSettersBuilder<TCronJob> setters,
        string fingerprint
    )
        where TCronJob : CronJobEntity
    {
        setters
            .SetProperty(x => x.EvaluationFingerprint, fingerprint)
            .SetProperty(x => x.FingerprintFailureCount, 0)
            .SetProperty(x => x.FingerprintRetryAfterUtc, (DateTime?)null);
    }

    /// <summary>
    /// Ends the row's lease, if one is active, as part of an out-of-band write (a sweep, a cancel, an edit), so the
    /// attempt that held it is refused at its next renewal or settlement. The holder and expiry stay as they were.
    /// </summary>
    /// <remarks>
    /// Each assignment reads the row's values from before the update, so the state and the takeover count are judged
    /// against the same lease.
    /// </remarks>
    internal static UpdateSettersBuilder<TTimeJob> EndLease<TTimeJob>(
        this UpdateSettersBuilder<TTimeJob> setters,
        RowLeaseState state
    )
        where TTimeJob : TimeJobEntity<TTimeJob>
    {
        return setters
            .SetProperty(x => x.Lease.State, x => x.Lease.State == RowLeaseState.Active ? state : x.Lease.State)
            .SetProperty(
                x => x.Lease.TakeoverCount,
                x => x.Lease.State == RowLeaseState.Active ? 0 : x.Lease.TakeoverCount
            );
    }

    /// <inheritdoc cref="EndLease{TTimeJob}(UpdateSettersBuilder{TTimeJob},RowLeaseState)"/>
    internal static UpdateSettersBuilder<CronJobOccurrenceEntity<TCronJob>> EndLease<TCronJob>(
        this UpdateSettersBuilder<CronJobOccurrenceEntity<TCronJob>> setters,
        RowLeaseState state
    )
        where TCronJob : CronJobEntity
    {
        return setters
            .SetProperty(x => x.Lease.State, x => x.Lease.State == RowLeaseState.Active ? state : x.Lease.State)
            .SetProperty(
                x => x.Lease.TakeoverCount,
                x => x.Lease.State == RowLeaseState.Active ? 0 : x.Lease.TakeoverCount
            );
    }

    /// <summary>
    /// Renders the columns <paramref name="functionContext"/> updates on a cron occurrence, for the lease transition
    /// that writes them. Releasing the lock is the transition's job, not an assignment.
    /// </summary>
    internal static JobsRowSet CronOccurrenceWrite<TCronJob>(DbContext context, JobExecutionState functionContext)
        where TCronJob : CronJobEntity
    {
        var set = JobsRowSet.For<CronJobOccurrenceEntity<TCronJob>>(context);
        var propsToUpdate = functionContext.PropertiesToUpdate;

        if (propsToUpdate.Contains(nameof(JobExecutionState.Status)))
        {
            set.Value(nameof(CronJobOccurrenceEntity<>.Status), functionContext.Status);

            if (functionContext.Status == JobStatus.Skipped)
            {
                // A user-code skip is the occurrence's own verdict on its instant: it ran and chose not to act.
                // Nothing is owed, so the row accounts for the instant.
                set.Value(nameof(CronJobOccurrenceEntity<>.SkippedReason), functionContext.ExceptionDetails)
                    .Value(nameof(CronJobOccurrenceEntity<>.Disposition), CronOccurrenceDisposition.Accounted);
            }
        }

        _RenderCommon(set, functionContext);

        if (propsToUpdate.Contains(nameof(JobExecutionState.ExecutionTime)))
        {
            set.Value(nameof(CronJobOccurrenceEntity<>.ExecutionTime), functionContext.ExecutionTime);
        }

        // Every write stamps UpdatedAt from the database clock; an occurrence's UpdatedAt otherwise freezes at its
        // last claim for the whole run.
        set.DatabaseNow(nameof(CronJobOccurrenceEntity<>.UpdatedAt));

        return set;
    }

    /// <summary>
    /// Renders the columns <paramref name="functionContext"/> updates on a time job, for the lease transition that
    /// writes them. Releasing the lock is the transition's job, not an assignment.
    /// </summary>
    internal static JobsRowSet TimeJobWrite<TTimeJob>(DbContext context, JobExecutionState functionContext)
        where TTimeJob : TimeJobEntity<TTimeJob>
    {
        var set = JobsRowSet.For<TTimeJob>(context);

        if (functionContext.PropertiesToUpdate.Contains(nameof(JobExecutionState.Status)))
        {
            set.Value(nameof(TimeJobEntity.Status), functionContext.Status);

            if (functionContext.Status == JobStatus.Skipped)
            {
                set.Value(nameof(TimeJobEntity.SkippedReason), functionContext.ExceptionDetails);
            }
        }

        _RenderCommon(set, functionContext);

        // UpdatedAt comes from the database clock, not a bound app-clock value, so the column stays monotonic per row
        // whichever node writes it.
        set.DatabaseNow(nameof(TimeJobEntity.UpdatedAt));

        return set;
    }

    // The columns a time job and a cron occurrence share, under the same property names on both entities.
    private static void _RenderCommon(JobsRowSet set, JobExecutionState functionContext)
    {
        var propsToUpdate = functionContext.PropertiesToUpdate;

        if (propsToUpdate.Contains(nameof(JobExecutionState.ExecutedAt)))
        {
            set.Value(nameof(TimeJobEntity.ExecutedAt), functionContext.ExecutedAt);
        }

        if (
            propsToUpdate.Contains(nameof(JobExecutionState.ExceptionDetails))
            && functionContext.Status != JobStatus.Skipped
        )
        {
            set.Value(nameof(TimeJobEntity.ExceptionMessage), functionContext.ExceptionDetails);
        }

        if (propsToUpdate.Contains(nameof(JobExecutionState.FailureFingerprint)))
        {
            set.Value(nameof(TimeJobEntity.FailureFingerprint), functionContext.FailureFingerprint);
        }

        if (propsToUpdate.Contains(nameof(JobExecutionState.ElapsedTime)))
        {
            set.Value(nameof(TimeJobEntity.ElapsedTime), functionContext.ElapsedTime);
        }

        if (propsToUpdate.Contains(nameof(JobExecutionState.RetryCount)))
        {
            set.Value(nameof(TimeJobEntity.RetryCount), functionContext.RetryCount);
        }

        // The report the throttle had not written yet when the run ended, stamped with the database clock.
        if (propsToUpdate.Contains(nameof(JobExecutionState.Progress)) && functionContext.Progress is { } progress)
        {
            set.Value(nameof(TimeJobEntity.ProgressPercent), progress.Percent)
                .Value(nameof(TimeJobEntity.ProgressMessage), progress.Message)
                .DatabaseNow(nameof(TimeJobEntity.ProgressUpdatedAt));
        }
    }
}
