// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

namespace Headless.CommitCoordination.SqlServer;

/// <summary>
/// The scope handed to a caller that enlists a <c>SqlTransaction</c> directly. It delegates to the core scope and
/// adds the explicit-signal contract's diagnostic: raw SqlClient exposes no commit edge this package observes, so
/// the caller must signal the outcome; a dispose that arrives without a signal after the transaction has already
/// completed is almost certainly a forgotten signal, and is logged as a warning before the enlisted work is
/// discarded.
/// </summary>
/// <remarks>
/// An un-signalled dispose while the transaction is still open is the normal failure path (the operation threw
/// before commit) and logs nothing. Disposal stays synchronous so the ambient pop runs in the caller's frame.
/// </remarks>
internal sealed partial class SqlServerCommitScope(
    ICommitScope inner,
    Func<bool> isTransactionCompleted,
    ILogger logger
) : ICommitScope
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

    private void _WarnIfUnsignalledAfterCompletion()
    {
        // Active here means nobody claimed an outcome; the dispose below will claim rollback and discard the work.
        if (inner.Coordinator.State == CommitCoordinatorState.Active && isTransactionCompleted())
        {
            LogUnsignalledDisposeAfterCompletedTransaction(logger);
        }
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "A SQL Server commit coordination scope was disposed without a signal after its transaction had "
            + "already completed; the enlisted work is discarded. Call ICommitScope.SignalAsync with the transaction's "
            + "outcome after Commit or Rollback, or use SqlConnection.ExecuteCoordinatedTransactionAsync, which "
            + "signals for you."
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void LogUnsignalledDisposeAfterCompletedTransaction(ILogger logger);
}
