// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Headless.CommitCoordination;
using Headless.Jobs.BackgroundServices;
using Headless.Jobs.Entities;
using Headless.Jobs.Interfaces;
using Microsoft.Extensions.Logging;

namespace Headless.Jobs.Managers;

// Commit-coordination routing for atomic enqueue: synchronous capture of the ambient coordinator, the fail-loud
// mis-wire/dead-transaction checks, the synchronous commit callback that hands post-commit work to the hosted worker,
// and the signal kinds it hands over. The main JobsManager partial holds the add-job flow that routes through this
// seam.
internal sealed partial class JobsManager<TTimeJob, TCronJob>
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    // Captured ambient coordinator + live relational transaction for one coordinated enqueue. Captured SYNCHRONOUSLY
    // in the caller's frame before the first await — re-reading ICurrentCommitCoordinator.Current after an await could
    // observe a torn-down AsyncLocal scope and silently take the direct path, breaking atomicity.
    private readonly record struct CoordinatedJobContext(
        ICommitCoordinator Coordinator,
        CapturedRelationalContext Relational,
        ICoordinatedJobWriter<TTimeJob, TCronJob> Writer,
        bool RequireSavepoints
    );

    // Routing decision read once, synchronously, before any await (KTD-1):
    //  - null  → no relational capability and atomicity was not required → today's direct path.
    //  - value → a live relational transaction is present → write rows inside it and defer side effects to commit.
    // Throws when a relational capability is present but its transaction is dead/completed: the caller opened a
    // transaction expecting atomicity, so silent fallback would reintroduce the divergence this feature prevents (KTD-2).
    // Jobs propagate write faults so callers cannot mistake a failed enqueue for a successfully enlisted deadline.
    private CoordinatedJobContext? _TryCaptureCoordinatedContext(
        bool requireAtomicEnlistment = false,
        bool requireSavepoints = false
    )
    {
        var coordinator = _currentCommitCoordinator.Current;

        if (coordinator is null)
        {
            _RejectMissingAtomicCapability(requireAtomicEnlistment);
            return null;
        }

        if (!coordinator.TryGetCapability<IRelationalCommitContext>(out var relational))
        {
            _RejectMissingAtomicCapability(requireAtomicEnlistment);
            // A coordinated scope without a relational capability (e.g. a messaging-only scope): the coordinator is an
            // ambient scope any subsystem may open, so jobs must not make it infectious — fall back to direct insert.
            return null;
        }

        if (relational.Transaction is null)
        {
            throw new InvalidOperationException(
                "A relational commit coordinator is active but its transaction is no longer live, so the job row "
                    + "cannot be enlisted atomically. Enqueue inside a live coordinated transaction, or call AddAsync "
                    + "outside the coordinated scope."
            );
        }

        // Resolve the writer here — still synchronous, before the caller's first await — so a relational coordinator
        // wired to a non-coordinated provider fails loud at capture (KTD-2) rather than mid-write.
        var writer = _RequireCoordinatedWriter();
        var captured = new CapturedRelationalContext(relational);
        writer.ValidateContext(captured, requireSavepoints);
        return new CoordinatedJobContext(coordinator, captured, writer, requireSavepoints);
    }

    private static void _RejectMissingAtomicCapability(bool required)
    {
        if (required)
        {
            throw new InvalidOperationException(
                "Required atomic Jobs scheduling needs an active commit coordinator with a compatible live relational transaction."
            );
        }
    }

    private void _PrepareCoordinatedWrite(CoordinatedJobContext context)
    {
        if (!ReferenceEquals(_currentCommitCoordinator.Current, context.Coordinator))
        {
            throw new InvalidOperationException(
                "The captured Jobs commit coordinator changed during scheduling; atomic enlistment cannot continue."
            );
        }
        context.Relational.Validate();
        context.Writer.ValidateContext(context.Relational, context.RequireSavepoints);
        // Jobs writes use a separate context; an owned business save cannot recreate them after rollback.
        context.Coordinator.GetOrAdd(static _ => new CommitRetryGuard()).PreventRetry();
    }

    private sealed class CapturedRelationalContext : IRelationalCommitContext
    {
        private readonly IRelationalCommitContext _original;
        public DbConnection Connection { get; }
        public DbTransaction Transaction { get; }

        public CapturedRelationalContext(IRelationalCommitContext original)
        {
            _original = original;
            Connection =
                original.Connection
                ?? throw new InvalidOperationException("The relational Jobs transaction has no live connection.");
            Transaction =
                original.Transaction
                ?? throw new InvalidOperationException("The relational Jobs transaction is no longer live.");
            Validate();
        }

        public void Validate()
        {
            if (
                !ReferenceEquals(_original.Connection, Connection)
                || !ReferenceEquals(_original.Transaction, Transaction)
                || Connection.State != ConnectionState.Open
                || !ReferenceEquals(Transaction.Connection, Connection)
            )
            {
                throw new InvalidOperationException(
                    "The captured Jobs connection/transaction is closed, completed, or changed; atomic enlistment cannot continue."
                );
            }
        }
    }

    private ICoordinatedJobWriter<TTimeJob, TCronJob> _RequireCoordinatedWriter()
    {
        if (persistenceProvider is ICoordinatedJobWriter<TTimeJob, TCronJob> writer)
        {
            return writer;
        }

        // Relational coordinator active, but the configured provider cannot write inside the ambient transaction
        // (e.g. the in-memory provider). This is a mis-wire, not a fallback — fail loud rather than insert
        // non-atomically.
        throw new InvalidOperationException(
            "A relational commit coordinator is active but the configured job persistence provider does not support "
                + "coordinated writes. The coordinated-enqueue path requires the EF Core operational store "
                + "(UseEntityFramework)."
        );
    }

    // Bound for the one side effect that stays on the commit path (the cron-expressions cache invalidation). The
    // coordinator drains OnCommit callbacks with CancellationToken.None, so the incoming token never carries a
    // deadline; without an independent one a stalled cache would hold the commit thread, DI scope, and connection.
    private static readonly TimeSpan _CronCacheInvalidationDeadline = JobsPostCommitSignalService.SignalDeadline;

    // Registers a coordinated write's post-commit signal. The callback is synchronous: it hands the worker a signal
    // and returns, so dispatch, scheduler restart, and dashboard notification never run on the caller's commit. The
    // row is already durable when the worker runs them, so a dropped or failed signal cannot roll the commit back —
    // the scheduler's polling sweep is the recovery path (KTD-4).
    private void _SignalOnCommit(ICommitCoordinator coordinator, JobsPostCommitSignal signal)
    {
        // The IDisposable unsubscribe handle is intentionally discarded (as in MessageOutboxBuffer): once the row is
        // written the signal must fire unconditionally on commit, so there is nothing to cancel.
        coordinator.OnCommit(
            (_, _) =>
            {
                _postCommitSignals.TrySignal(signal);

                return ValueTask.CompletedTask;
            }
        );
    }

    // Cron variant: the cron-expressions cache invalidation (which the direct path's InsertCronJobsAsync runs after
    // SaveChanges) fires inline on commit — never on a pre-commit snapshot, and never through the drop-on-full channel,
    // because the poll sweep reads THROUGH that distributed cache and would not recover a dropped invalidation (R10).
    // It is bounded so a stalled cache releases the commit; the durable store stays authoritative either way.
    private void _SignalCronOnCommit(
        ICommitCoordinator coordinator,
        ICoordinatedJobWriter<TTimeJob, TCronJob> writer,
        JobsPostCommitSignal signal
    )
    {
        coordinator.OnCommit(
            async (_, _) =>
            {
                await _InvalidateCronExpressionsCacheBoundedAsync(writer, signal.JobScope).ConfigureAwait(false);
                _postCommitSignals.TrySignal(signal);
            }
        );
    }

    private async Task _InvalidateCronExpressionsCacheBoundedAsync(
        ICoordinatedJobWriter<TTimeJob, TCronJob> writer,
        string jobScope
    )
    {
        Task? invalidation = null;

        try
        {
            invalidation = writer.InvalidateCronExpressionsCacheAsync();
            await invalidation.WaitAsync(_CronCacheInvalidationDeadline, timeProvider).ConfigureAwait(false);
        }
        catch (TimeoutException) when (invalidation is { IsCompleted: false })
        {
            // The removal is not cancellable, so it completes unobserved; only its late fault is worth a log line.
            Log.CronCacheInvalidationTimedOut(_logger, jobScope, _CronCacheInvalidationDeadline);
            _ = invalidation.ContinueWith(
                static (settled, state) =>
                {
                    var (logger, scope) = ((ILogger, string))state!;
                    Log.CronCacheInvalidationFailed(logger, scope, settled.Exception!);
                },
                (_logger, jobScope),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default
            );
        }
        catch (Exception e)
        {
            Log.CronCacheInvalidationFailed(_logger, jobScope, e);
        }
    }

    // Signal kinds. Each carries what the worker needs to run the existing side-effect methods against rows that are
    // already committed; the worker supplies the clock read at processing time.

    private sealed record TimeJobCommittedSignal(
        JobsManager<TTimeJob, TCronJob> Manager,
        TTimeJob Entity,
        DateTime ExecutionTimeUtc
    ) : JobsPostCommitSignal(Entity.Id.ToString())
    {
        public override Task RunAsync(DateTimeOffset now, CancellationToken cancellationToken)
        {
            return Manager._RunTimeJobSideEffectsAsync(Entity, now, ExecutionTimeUtc, cancellationToken);
        }
    }

    private readonly record struct CommittedTimeJob(Guid Id, DateTime ExecutionTimeUtc);

    private sealed record TimeJobsBatchCommittedSignal(JobsManager<TTimeJob, TCronJob> Manager, CommittedTimeJob[] Jobs)
        : JobsPostCommitSignal($"time batch ({Jobs.Length})")
    {
        public override Task RunAsync(DateTimeOffset now, CancellationToken cancellationToken)
        {
            // Split immediate vs. later against the worker's clock, not the enqueue-time one: a commit can land long
            // after the enqueue, and a job that became due meanwhile must be acquired now rather than scheduled.
            var nowUtc = now.UtcDateTime;
            var immediateTickers = new List<Guid>();
            var earliestForNonImmediate = default(DateTime);

            foreach (var job in Jobs)
            {
                if (job.ExecutionTimeUtc <= nowUtc.AddSeconds(1))
                {
                    immediateTickers.Add(job.Id);
                }
                else if (earliestForNonImmediate == default || job.ExecutionTimeUtc <= earliestForNonImmediate)
                {
                    earliestForNonImmediate = job.ExecutionTimeUtc;
                }
            }

            return Manager._RunTimeJobsBatchSideEffectsAsync(
                immediateTickers,
                earliestForNonImmediate,
                cancellationToken
            );
        }
    }

    private sealed record CronJobsCommittedSignal(
        JobsManager<TTimeJob, TCronJob> Manager,
        TCronJob[] Entities,
        DateTime? PersistedEarliestNextDueUtc,
        string Scope
    ) : JobsPostCommitSignal(Scope)
    {
        public override Task RunAsync(DateTimeOffset now, CancellationToken cancellationToken)
        {
            return Manager._RunCommittedCronJobsSideEffectsAsync(
                Entities,
                PersistedEarliestNextDueUtc,
                cancellationToken
            );
        }
    }

    // Cron side effects after commit (the cache invalidation already ran on the commit callback).
    private async Task _RunCommittedCronJobsSideEffectsAsync(
        TCronJob[] entities,
        DateTime? persistedEarliestNextDueUtc,
        CancellationToken cancellationToken
    )
    {
        if (entities.Length == 0)
        {
            return;
        }

        // The store-anchored position the write persisted. Recomputing it here from this node's clock would arm the
        // wake against a projection the row does not carry.
        _jobsHostScheduler.RestartIfNeeded(persistedEarliestNextDueUtc);

        foreach (var entity in entities)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await notificationHubSender.AddCronJobNotifyAsync(entity).ConfigureAwait(false);
        }
    }

    private sealed record ScheduleChangedSignal(JobsManager<TTimeJob, TCronJob> Manager, string RunId)
        : JobsPostCommitSignal(RunId)
    {
        public override Task RunAsync(DateTimeOffset now, CancellationToken cancellationToken)
        {
            Manager._jobsHostScheduler.Restart();

            return Task.CompletedTask;
        }
    }

    private static partial class Log
    {
        [LoggerMessage(
            LogLevel.Warning,
            "Cron-expressions cache invalidation failed after committing {JobScope}. The definition row is committed; "
                + "the cache entry is stale until it expires or the next definition write invalidates it."
        )]
        public static partial void CronCacheInvalidationFailed(ILogger logger, string jobScope, Exception exception);

        [LoggerMessage(
            LogLevel.Warning,
            "Cron-expressions cache invalidation for {JobScope} did not finish within {Deadline}; the commit was "
                + "released and the removal completes unobserved."
        )]
        public static partial void CronCacheInvalidationTimedOut(ILogger logger, string jobScope, TimeSpan deadline);
    }
}
