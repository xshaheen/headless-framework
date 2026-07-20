// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.CommitCoordination;

internal sealed class CommitScope(CommitCoordinator coordinator, IServiceProvider services, IDisposable ambientHandle)
    : ICommitScope
{
    private int _signaled;
    private int _disposed;

    public ICommitCoordinator Coordinator => coordinator;

    /// <summary>
    /// Cleanup invoked by the path that claims the un-signalled abandon — after the offloaded rollback drain
    /// completes, or inline when no drain was claimable. Lets a wrapper (TrackedCommitScope) transfer ownership
    /// of drain-visible resources (the owned DI scope) to the drain instead of the disposing frame.
    /// </summary>
    internal Action? AbandonCleanup { get; set; }

    /// <summary>
    /// Claims the single scope-level signal latch. The first caller (an explicit signal, or an un-signalled
    /// disposal) wins and proceeds to claim the coordinator's terminal outcome; later callers no-op. This is the
    /// only writer of <c>_signaled</c>.
    /// </summary>
    private bool _TryClaimSignal()
    {
        return Interlocked.Exchange(ref _signaled, 1) == 0;
    }

    public ValueTask SignalAsync(CommitOutcome outcome)
    {
        return SignalAsync(outcome, out _);
    }

    /// <summary>
    /// Signals, reporting whether THIS call claimed the scope-level signal latch. The claim is synchronous, so the
    /// out value is settled before the returned drain task is awaited; wrappers use it to decide resource ownership
    /// (only the claiming call's await spans the full drain).
    /// </summary>
    internal ValueTask SignalAsync(CommitOutcome outcome, out bool claimedSignal)
    {
        CommitOutcomeValidation.ThrowIfNotTerminal(outcome);

        claimedSignal = _TryClaimSignal();

        if (!claimedSignal)
        {
            return ValueTask.CompletedTask;
        }

        // Claim the terminal outcome synchronously (on this thread, e.g. the commit edge), then drain asynchronously.
        // The synchronous claim guarantees a racing Dispose observes the outcome and never rolls back committed work.
        return coordinator.TryClaimTerminal(outcome, out var claim)
            ? CommitCoordinator.DrainAsync(claim, services)
            : ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        // Ambient-frame disposal is the caller's sole responsibility and must happen synchronously in the caller's
        // own frame so ICurrentCommitCoordinator.Current is restored deterministically — never on an off-thread drain.
        ambientHandle.Dispose();

        if (!_TryClaimSignal())
        {
            // An explicit signal claimed the drain; that path owns any transferred cleanup.
            coordinator.DisposePromotedRegistrations();

            return;
        }

        if (coordinator.TryClaimTerminal(CommitOutcome.RolledBack, out var claim))
        {
            // Un-signalled abandon: discard the work by claiming rollback. Offload the drain so disposing under a
            // captured SynchronizationContext cannot deadlock or stall the disposing thread. Promoted registrations
            // (and any transferred cleanup, e.g. the owned DI scope the drain resolves callbacks from) are disposed
            // only AFTER the offloaded drain runs — disposing them synchronously here would tear down state the
            // in-flight drain still uses (the DisposeAsync path orders these the same way).
            var abandonCleanup = AbandonCleanup;

            CommitCoordinator.DrainInBackground(
                claim,
                services,
                () =>
                {
                    coordinator.DisposePromotedRegistrations();
                    abandonCleanup?.Invoke();
                }
            );

            return;
        }

        coordinator.DisposePromotedRegistrations();
        AbandonCleanup?.Invoke();
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return ValueTask.CompletedTask;
        }

        // This method is intentionally NOT async: the ambient pop must run in the caller's own execution context so
        // the AsyncLocal restore propagates back to the caller. An async state machine would strand the pop in its
        // own context (the exact AsyncLocal hazard this design exists to avoid). Only the abandon drain is async.
        ambientHandle.Dispose();

        if (_TryClaimSignal() && coordinator.TryClaimTerminal(CommitOutcome.RolledBack, out var claim))
        {
            // Unlike the sync Dispose abandon path, AbandonCleanup is intentionally NOT invoked here. The sync path
            // offloads the drain to the background and uses AbandonCleanup to hand owned-resource disposal to that
            // off-thread drain; this path awaits the drain inline, so the async wrapper (TrackedCommitScope) disposes
            // the owned DI scope itself once this completes. AbandonCleanup is therefore sync-Dispose-only by design.
            return _DrainAndDisposePromotedAsync(claim);
        }

        coordinator.DisposePromotedRegistrations();

        return ValueTask.CompletedTask;
    }

    private async ValueTask _DrainAndDisposePromotedAsync(CommitCoordinator.CommitTerminalClaim claim)
    {
        try
        {
            await CommitCoordinator.DrainAsync(claim, services).ConfigureAwait(false);
        }
        finally
        {
            coordinator.DisposePromotedRegistrations();
        }
    }
}
