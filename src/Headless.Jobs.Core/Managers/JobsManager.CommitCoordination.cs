// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination;
using Headless.Jobs.Entities;
using Headless.Jobs.Interfaces;
using Microsoft.Extensions.Logging;

namespace Headless.Jobs.Managers;

// Commit-coordination routing for atomic enqueue: synchronous capture of the ambient coordinator, the fail-loud
// mis-wire/dead-transaction checks, post-commit side-effect deferral, and the coordinated cron side effects. The main
// JobsManager partial holds the add-job flow that routes through this seam.
internal sealed partial class JobsManager<TTimeJob, TCronJob>
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    // Captured ambient coordinator + live relational transaction for one coordinated enqueue. Captured SYNCHRONOUSLY
    // in the caller's frame before the first await — re-reading ICurrentCommitCoordinator.Current after an await could
    // observe a torn-down AsyncLocal scope and silently take the direct path, breaking atomicity.
    private readonly record struct CoordinatedJobContext(
        ICommitCoordinator Coordinator,
        IRelationalCommitContext Relational,
        ICoordinatedJobWriter<TTimeJob, TCronJob> Writer
    );

    // Routing decision read once, synchronously, before any await (KTD-1):
    //  - null  → no coordinator, or a coordinated scope with no relational capability → today's direct path.
    //  - value → a live relational transaction is present → write rows inside it and defer side effects to commit.
    // Throws when a relational capability is present but its transaction is dead/completed: the caller opened a
    // transaction expecting atomicity, so silent fallback would reintroduce the divergence this feature prevents (KTD-2).
    // NOTE: this is a deliberate divergence from messaging's OutboxMessageWriter, which falls back rather than throwing.
    // Jobs fail loud here (and Add propagates write faults) because a swallowed enqueue would let the caller commit its
    // domain writes without the job row — the exact split this feature exists to prevent. Do not "align" the two.
    private CoordinatedJobContext? _TryCaptureCoordinatedContext()
    {
        var coordinator = _currentCommitCoordinator.Current;

        if (coordinator is null)
        {
            return null;
        }

        if (!coordinator.TryGetCapability<IRelationalCommitContext>(out var relational))
        {
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
        return new CoordinatedJobContext(coordinator, relational, _RequireCoordinatedWriter());
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

    // Liveness bound for the post-commit drain. The coordinator drains OnCommit callbacks with CancellationToken.None
    // (a committed job's dispatch must not be abandoned because the request was cancelled), so the incoming token can
    // never carry a deadline. Without an independent one, a hung dispatch / notify / cache call would hold the commit
    // thread, DI scope, and DB connection indefinitely. Mirrors MessageOutboxBuffer's _flushTimeout. The constant
    // matches the fallback poll-sweep cadence; making it a configurable option is a clean follow-up.
    private static readonly TimeSpan _PostCommitDrainTimeout = TimeSpan.FromSeconds(30);

    // Registers a coordinated enqueue's side effects to run after the caller's transaction commits. The row is already
    // durable when these run, so a failure cannot roll the commit back: it is logged against the job scope and the
    // scheduler's polling sweep is the recovery path (KTD-4). Swallowing keeps one subsystem's deferred failure from
    // aborting the shared commit; the OnCommit interceptor would otherwise absorb it without the job identity.
    private void _DeferSideEffects(
        ICommitCoordinator coordinator,
        string jobScope,
        Func<CancellationToken, Task> sideEffects
    )
    {
        // The IDisposable unsubscribe handle is intentionally discarded (as in MessageOutboxBuffer): once the row is
        // written the side effects must fire unconditionally on commit, so there is nothing to cancel.
        coordinator.OnCommit(
            // The drain passes CancellationToken.None (discarded), so bound the work with an independent deadline; the
            // side effects observe the timeout token, not the drain's.
            async (_, _) =>
            {
                using var timeoutCts = new CancellationTokenSource(_PostCommitDrainTimeout, timeProvider);

                try
                {
                    await sideEffects(timeoutCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
                {
                    // The deadline elapsed before the side effects finished (e.g. a hung dispatch). The row is
                    // committed and the fallback poll sweep recovers the deferred work, so this is a bounded-wait
                    // timeout — not the recoverable failure the Warning below is for.
                    Log.DeferredJobSideEffectsTimedOut(_logger, jobScope, _PostCommitDrainTimeout);
                }
                catch (Exception e)
                {
                    Log.DeferredJobSideEffectsFailed(_logger, jobScope, e);
                }
            }
        );
    }

    // Coordinated single-cron side effects, deferred to commit. The coordinated write is a pure row write, so the
    // cron-expressions cache invalidation (which the direct path's InsertCronJobs runs after SaveChanges) must fire
    // here — post-commit — never on a pre-commit snapshot (KTD-4).
    private async Task _RunCoordinatedCronJobSideEffectsAsync(
        ICoordinatedJobWriter<TTimeJob, TCronJob> writer,
        TCronJob entity,
        DateTime nextOccurrence,
        CancellationToken cancellationToken
    )
    {
        // Honor the drain deadline (the cron side-effect calls below are not themselves cancellable, so the token is
        // checked between steps) — symmetric with the time-job side-effect path.
        cancellationToken.ThrowIfCancellationRequested();
        await writer.InvalidateCronExpressionsCacheAsync().ConfigureAwait(false);
        _jobsHostScheduler.RestartIfNeeded(nextOccurrence);
        cancellationToken.ThrowIfCancellationRequested();
        await notificationHubSender.AddCronJobNotifyAsync(entity).ConfigureAwait(false);
    }

    // Coordinated batch-cron side effects, deferred to commit (cache invalidation post-commit per KTD-4).
    private async Task _RunCoordinatedCronJobsBatchSideEffectsAsync(
        ICoordinatedJobWriter<TTimeJob, TCronJob> writer,
        List<TCronJob> validEntities,
        List<DateTime> nextOccurrences,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        await writer.InvalidateCronExpressionsCacheAsync().ConfigureAwait(false);

        if (validEntities.Count != 0)
        {
            var earliestOccurrence = nextOccurrences.Min();
            _jobsHostScheduler.RestartIfNeeded(earliestOccurrence);

            foreach (var entity in validEntities)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await notificationHubSender.AddCronJobNotifyAsync(entity).ConfigureAwait(false);
            }
        }
    }

    private static partial class Log
    {
        [LoggerMessage(
            LogLevel.Warning,
            "Deferred post-commit side effects failed for {JobScope}. The job row is committed; the scheduler's "
                + "polling sweep is the recovery path."
        )]
        public static partial void DeferredJobSideEffectsFailed(ILogger logger, string jobScope, Exception exception);

        [LoggerMessage(
            LogLevel.Warning,
            "Deferred post-commit side effects for {JobScope} did not finish within {Timeout}. The job row is "
                + "committed; the scheduler's polling sweep is the recovery path."
        )]
        public static partial void DeferredJobSideEffectsTimedOut(ILogger logger, string jobScope, TimeSpan timeout);
    }
}
