// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.CommitCoordination;

internal sealed class CommitScope(
    CommitCoordinator coordinator,
    IServiceProvider services,
    IDisposable ambientHandle
) : ICommitScope
{
    private int _signaled;
    private int _disposed;

    public ICommitCoordinator Coordinator => coordinator;

    /// <summary>
    /// Claims the single scope-level signal latch. The first caller (an explicit signal, or an un-signalled
    /// disposal) wins and proceeds to claim the coordinator's terminal outcome; later callers no-op. This is the
    /// only writer of <c>_signaled</c>.
    /// </summary>
    private bool _TryClaimSignal()
    {
        return Interlocked.Exchange(ref _signaled, 1) == 0;
    }

    public ValueTask SignalAsync(CommitOutcome outcome, CancellationToken cancellationToken)
    {
        if (!_TryClaimSignal())
        {
            return ValueTask.CompletedTask;
        }

        // Claim the terminal outcome synchronously (on this thread, e.g. the commit edge), then drain asynchronously.
        // The synchronous claim guarantees a racing Dispose observes the outcome and never rolls back committed work.
        return coordinator.TryClaimTerminal(outcome, out var claim)
            ? CommitCoordinator.DrainAsync(claim, services, cancellationToken)
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

        if (_TryClaimSignal() && coordinator.TryClaimTerminal(CommitOutcome.RolledBack, out var claim))
        {
            // Un-signalled abandon: discard the work by claiming rollback. Offload the drain so disposing under a
            // captured SynchronizationContext cannot deadlock or stall the disposing thread. Promoted registrations
            // are disposed only AFTER the offloaded drain runs — disposing them synchronously here would mark a
            // child's promoted rollback callbacks IsDisposed before the drain reached them, silently dropping them
            // (the DisposeAsync path orders these the same way).
            CommitCoordinator.DrainInBackground(claim, services, coordinator.DisposePromotedRegistrations);

            return;
        }

        coordinator.DisposePromotedRegistrations();
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
            return _DrainAndDisposePromotedAsync(claim);
        }

        coordinator.DisposePromotedRegistrations();

        return ValueTask.CompletedTask;
    }

    private async ValueTask _DrainAndDisposePromotedAsync(CommitCoordinator.CommitTerminalClaim claim)
    {
        try
        {
            await CommitCoordinator.DrainAsync(claim, services, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            coordinator.DisposePromotedRegistrations();
        }
    }
}
