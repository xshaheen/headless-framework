// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.CommitCoordination;

internal sealed class CommitScope(CommitCoordinator coordinator, IDisposable ambientHandle) : ICommitScope
{
    private int _disposed;

    public ICommitCoordinator Coordinator => coordinator;

    public ValueTask SignalAsync(CommitOutcome outcome)
    {
        // The coordinator claims synchronously on this thread (e.g. the commit edge) so a racing Dispose observes
        // the outcome, and it owns the per-outcome idempotency — a repeated, conflicting, or post-dispose signal
        // needs no scope-level latch.
        return coordinator.SignalAsync(outcome);
    }

    public void Dispose()
    {
        if (!_TryBeginDispose())
        {
            return;
        }

        if (coordinator.TryClaimAbandon(out var claim))
        {
            // Un-signalled abandon: nothing runs, but scope-local state may be IAsyncDisposable. Offload so a
            // synchronous dispose under a captured SynchronizationContext cannot deadlock or stall the disposing
            // thread; the async path below awaits the same drain inline.
            CommitCoordinator.DrainInBackground(claim);
        }
    }

    public ValueTask DisposeAsync()
    {
        // Intentionally NOT async: the ambient pop must run in the caller's own execution context so the AsyncLocal
        // restore propagates back to the caller. An async state machine would strand the pop in its own context (the
        // exact AsyncLocal hazard this design exists to avoid). Only the abandon drain is asynchronous.
        if (!_TryBeginDispose())
        {
            return ValueTask.CompletedTask;
        }

        return coordinator.TryClaimAbandon(out var claim)
            ? CommitCoordinator.DrainAsync(claim)
            : ValueTask.CompletedTask;
    }

    /// <summary>
    /// Latches disposal atomically, then pops the ambient frame. An out-of-order pop throws and must leave the
    /// scope un-disposed and un-claimed so the owner can unwind the inner scope and dispose again, so the latch is
    /// released before the exception escapes; concurrent disposers still see exactly one winner.
    /// </summary>
    private bool _TryBeginDispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        {
            return false;
        }

        try
        {
            ambientHandle.Dispose();
        }
        catch
        {
            Volatile.Write(ref _disposed, 0);
            throw;
        }

        return true;
    }
}
