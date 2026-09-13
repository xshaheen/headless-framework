// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

namespace Headless.CommitCoordination.PostgreSql;

/// <summary>
/// The PostgreSQL flavour of the explicit-signal scope: the driver exposes no commit edge this package observes, so a
/// caller who enlists a transaction directly must signal the outcome, and an un-signalled dispose after the
/// transaction has already completed is logged as a forgotten signal.
/// </summary>
internal sealed partial class PostgreSqlCommitScope(
    ICommitScope inner,
    Func<bool> isTransactionCompleted,
    ILogger logger
) : ExplicitSignalCommitScope(inner, isTransactionCompleted)
{
    protected override void LogUnsignalledDisposeAfterCompletedTransaction()
    {
        LogUnsignalledDisposeAfterCompletedTransaction(logger);
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "A PostgreSQL commit coordination scope was disposed without a signal after its transaction had "
            + "already completed; the enlisted work is discarded. Call ICommitScope.SignalAsync with the transaction's "
            + "outcome after Commit or Rollback, or use NpgsqlConnection.ExecuteCoordinatedTransactionAsync, which "
            + "signals for you."
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void LogUnsignalledDisposeAfterCompletedTransaction(ILogger logger);
}
