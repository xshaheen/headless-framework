// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.CommitCoordination;

/// <summary>
/// The scope a raw-ADO provider hands to a caller that enlists a transaction directly. It delegates to the core
/// scope and adds the explicit-signal contract's diagnostic: the driver exposes no commit edge the provider can
/// observe, so the caller must signal the outcome; a dispose that arrives without a signal after the transaction
/// has already completed is almost certainly a forgotten signal, and the provider logs it as a warning before the
/// enlisted work is discarded.
/// </summary>
/// <remarks>
/// An un-signalled dispose while the transaction is still open is the normal failure path (the operation threw
/// before commit) and logs nothing. Disposal stays synchronous so the ambient pop runs in the caller's frame.
/// </remarks>
internal abstract class ExplicitSignalCommitScope(ICommitScope inner, Func<bool> isTransactionCompleted) : ICommitScope
{
    public ICommitCoordinator Coordinator => inner.Coordinator;

    public ValueTask SignalAsync(CommitOutcome outcome)
    {
        return inner.SignalAsync(outcome);
    }

    public void Dispose()
    {
        _WarnIfUnsignalledAfterCompletion();
        inner.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        _WarnIfUnsignalledAfterCompletion();

        return inner.DisposeAsync();
    }

    /// <summary>Logs the provider-specific forgotten-signal warning, naming the helper that signals for the caller.</summary>
    protected abstract void LogUnsignalledDisposeAfterCompletedTransaction();

    private void _WarnIfUnsignalledAfterCompletion()
    {
        // Active here means nobody claimed an outcome; the dispose below will claim rollback and discard the work.
        if (inner.Coordinator.State == CommitCoordinatorState.Active && isTransactionCompleted())
        {
            LogUnsignalledDisposeAfterCompletedTransaction();
        }
    }
}
