// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Headless.Jobs.BackgroundServices;
using Headless.Jobs.Entities;
using Headless.Jobs.Interfaces;
using Headless.UnitOfWork;
using Microsoft.Extensions.Logging;

namespace Headless.Jobs.Managers;

// Unit-of-work routing for atomic enqueue: capture of the caller-supplied IUnitOfWork?, the guarantee-matrix
// fail-loud checks, the synchronous OnCompleted callback that hands post-commit work to the hosted worker, and
// the signal kinds it hands over. The main JobsManager partial holds the add-job flow that routes through this seam.
// The core takes IUnitOfWork? as an explicit argument — it never resolves an ambient coordinator itself.
internal sealed partial class JobsManager<TTimeJob, TCronJob>
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    // Captured unit of work + live relational resource for one coordinated enqueue.
    private readonly record struct CoordinatedJobContext(
        IUnitOfWork UnitOfWork,
        CapturedRelationalResource Relational,
        ICoordinatedJobWriter<TTimeJob, TCronJob> Writer,
        bool RequireSavepoints
    );

    // Routing decision. The receiver decides enlistment: the autonomous facade passes no unit and takes the
    // direct path unless the function is TransactionEnlistment.Required, which refuses it before any effect;
    // the bound facade behind unit.Jobs passes its unit and must enlist — a unit with no joinable relational
    // resource, or an incompatible or dead one, throws rather than degrading to a standalone row.
    private CoordinatedJobContext? _TryCaptureCoordinatedContext(
        IUnitOfWork? unitOfWork,
        TransactionEnlistment enlistment,
        string function,
        bool requireSavepoints
    )
    {
        if (unitOfWork is null)
        {
            _RejectAutonomousReceiver(enlistment, function);
            return null;
        }

        if (unitOfWork is not { State: UnitOfWorkState.Active, Resource: IRelationalUnitOfWorkResource relational })
        {
            throw new InvalidOperationException(
                $"Scheduling '{function}' through unit.Jobs requires the unit of work to carry a live relational "
                    + "resource for the job store, but this one has none. Begin the unit of work on the job store's "
                    + "database (BeginAsync(db) or RunAsync(db, …)), or schedule through an injected scheduler for an "
                    + "autonomous write."
            );
        }

        // Resolve the writer here — still synchronous, before the caller's first await — so a relational unit of
        // work wired to a non-coordinated provider fails loud at capture rather than mid-write.
        var writer = _RequireCoordinatedWriter();
        var captured = new CapturedRelationalResource(relational);
        writer.ValidateContext(captured, requireSavepoints);
        return new CoordinatedJobContext(unitOfWork, captured, writer, requireSavepoints);
    }

    private static void _RejectAutonomousReceiver(TransactionEnlistment enlistment, string function)
    {
        if (enlistment == TransactionEnlistment.Required)
        {
            throw new InvalidOperationException(
                $"Scheduling '{function}' requires a unit of work (TransactionEnlistment.Required), so it cannot run "
                    + "through an injected scheduler or manager. Schedule it through unit.Jobs on the unit of work "
                    + "the write must join, or register the function with TransactionEnlistment.Optional."
            );
        }
    }

    // A plain State == Active re-validation before the write: there is no ambient coordinator to drift from; the
    // only failure mode left is the unit having completed under the caller (racing CompleteAsync elsewhere).
    private static void _PrepareCoordinatedWrite(CoordinatedJobContext context)
    {
        if (context.UnitOfWork.State != UnitOfWorkState.Active)
        {
            throw new InvalidOperationException(
                "The active unit of work is no longer active; the job row cannot be enlisted atomically."
            );
        }
        context.Relational.Validate();
        context.Writer.ValidateContext(context.Relational, context.RequireSavepoints);

        // Jobs writes use a separate context, so a replay cannot restore them from the retained tracker; they have
        // to be re-run. Replay re-runs the block that owns the unit: an owned unit (BeginAsync / RunAsync) is the
        // caller's block, which schedules again, so it stays replayable. An observed unit belongs to someone
        // else's commit edge — the EF save pipeline enlisting its own save — which replays without re-running the
        // domain-event handler that scheduled, so that write must end replay before it lands.
        if (!context.Relational.IsOwned)
        {
            context.UnitOfWork.PreventRetry();
        }
    }

    // Defensive snapshot mirroring the pre-existing capture: re-validates connection/transaction identity and
    // liveness before every write, and throws the shared "incompatible resource" remedy when the caller's resource
    // no longer matches what was captured (closed connection, replaced transaction).
    private sealed class CapturedRelationalResource : IRelationalUnitOfWorkResource
    {
        private readonly IRelationalUnitOfWorkResource _original;
        public DbConnection Connection { get; }
        public DbTransaction Transaction { get; }
        public bool IsOwned => _original.IsOwned;
        public bool IsTransactionCompleted => _original.IsTransactionCompleted;

        public CapturedRelationalResource(IRelationalUnitOfWorkResource original)
        {
            _original = original;
            Connection = original.Connection;
            Transaction = original.Transaction;
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
                    "The active unit of work's transaction belongs to another database or is no longer live "
                        + "(closed, completed, or changed), so the Jobs write cannot enlist. Use the same database, "
                        + "or schedule through an injected scheduler for an autonomous write."
                );
            }
        }

        public ValueTask CommitAsync(CancellationToken cancellationToken) => _original.CommitAsync(cancellationToken);

        public ValueTask RollbackAsync(CancellationToken cancellationToken) =>
            _original.RollbackAsync(cancellationToken);
    }

    private ICoordinatedJobWriter<TTimeJob, TCronJob> _RequireCoordinatedWriter()
    {
        if (persistenceProvider is ICoordinatedJobWriter<TTimeJob, TCronJob> writer)
        {
            return writer;
        }

        // A joinable relational unit of work is active, but the configured provider cannot write inside it (e.g.
        // the in-memory provider). This is a mis-wire, not a fallback — fail loud rather than insert non-atomically.
        throw new InvalidOperationException(
            "An active unit of work has a joinable relational resource but the configured job persistence provider "
                + "does not support coordinated writes. The coordinated-enqueue path requires the EF Core "
                + "operational store (UseEntityFramework)."
        );
    }

    // Bound for the one side effect that stays on the commit path (the cron-expressions cache invalidation). The
    // unit of work drains OnCompleted callbacks without a cancellation token, so nothing external carries a
    // deadline; without an independent one a stalled cache would hold the commit thread, DI scope, and connection.
    private static readonly TimeSpan _CronCacheInvalidationDeadline = JobsPostCommitSignalService.SignalDeadline;

    // Registers a coordinated write's post-commit signal. The callback is synchronous: it hands the worker a signal
    // and returns, so dispatch, scheduler restart, and dashboard notification never run on the caller's commit. The
    // row is already durable when the worker runs them, so a dropped or failed signal cannot roll the commit back —
    // the scheduler's polling sweep is the recovery path.
    private void _SignalOnCommit(IUnitOfWork unitOfWork, JobsPostCommitSignal signal)
    {
        // The IDisposable unsubscribe handle is intentionally discarded (as in MessageOutboxBuffer): once the row is
        // written the signal must fire unconditionally on commit, so there is nothing to cancel.
        unitOfWork.OnCompleted(() =>
        {
            _postCommitSignals.TrySignal(signal);

            return ValueTask.CompletedTask;
        });
    }

    // Cron variant: the cron-expressions cache invalidation (which the direct path's InsertCronJobsAsync runs after
    // SaveChanges) fires inline on commit — never on a pre-commit snapshot, and never through the drop-on-full channel,
    // because the poll sweep reads THROUGH that distributed cache and would not recover a dropped invalidation.
    // It is bounded so a stalled cache releases the commit; the durable store stays authoritative either way.
    private void _SignalCronOnCommit(
        IUnitOfWork unitOfWork,
        ICoordinatedJobWriter<TTimeJob, TCronJob> writer,
        JobsPostCommitSignal signal
    )
    {
        unitOfWork.OnCompleted(async () =>
        {
            await _InvalidateCronExpressionsCacheBoundedAsync(writer, signal.JobScope).ConfigureAwait(false);
            _postCommitSignals.TrySignal(signal);
        });
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
            LateFaultObserver.ObserveLateFault(invalidation, _logger, jobScope, Log.CronCacheInvalidationFailed);
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
