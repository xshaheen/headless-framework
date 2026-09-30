// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using System.Net.Sockets;

namespace Headless.UnitOfWork.Internal;

/// <summary>
/// Classifies a fault raised by a commit as in-doubt — the commit request may have reached the database, but no
/// answer came back, so the transaction may or may not have committed — or as a definite failure.
/// </summary>
/// <remarks>
/// <para>
/// A definite failure is one the database answered (it reported an error, so it did not commit) or one raised on
/// the client before anything was sent (a completed transaction, an interceptor, a token already cancelled when the
/// commit began). Everything that loses the connection after the send is in-doubt: a transport fault under the
/// driver exception, a timeout, a cancellation that arrived mid-commit, a SqlClient error severe enough to close
/// the connection, or a server that reports it is shutting the connection down.
/// </para>
/// <para>
/// The walk is the transient classifier's: outer-first, stopping at the first <see cref="DbException" />, because
/// drivers wrap the socket error in their own exception. The classifier errs toward in-doubt: calling a definite
/// failure in-doubt costs an idempotency check, while calling an in-doubt commit a definite failure invites a retry
/// that applies the operation twice.
/// </para>
/// </remarks>
internal static class InDoubtCommitFaults
{
    private const int _SqlClientTimeoutNumber = -2;
    private const byte _SqlClientConnectionClosingSeverity = 20;

    /// <summary>Returns whether a fault raised by the commit leaves the transaction's outcome unknown.</summary>
    /// <param name="exception">The fault the resource's commit raised.</param>
    /// <param name="cancelledBeforeCommit">Whether the caller's token was already cancelled when the commit began.</param>
    public static bool IsInDoubt(Exception exception, bool cancelledBeforeCommit)
    {
        // Drivers check the token before sending, so a token cancelled up front sends nothing; one cancelled while
        // the commit was in flight may have been too late to stop it.
        if (exception is OperationCanceledException)
        {
            return !cancelledBeforeCommit;
        }

        if (RelationalTransientFaults.FindDatabaseException(exception) is not { } databaseException)
        {
            return _HasTransportFault(exception);
        }

        if (_HasTransportFault(databaseException.InnerException))
        {
            return true;
        }

        if (SqlServerTransientFaults.IsSqlClientException(databaseException))
        {
            return SqlServerTransientFaults.GetErrorNumber(databaseException) == _SqlClientTimeoutNumber
                || _GetSqlClientSeverity(databaseException) >= _SqlClientConnectionClosingSeverity;
        }

        if (databaseException.SqlState is { } sqlState)
        {
            // Class 08 is a connection exception; 57P0x is the server terminating the connection (administrator
            // command, crash, shutdown), which can arrive in answer to a commit it never finished.
            return sqlState.StartsWith("08", StringComparison.Ordinal)
                || sqlState.StartsWith("57P", StringComparison.Ordinal);
        }

        // A driver exception with no server state is the driver's own view of the wire. Npgsql, for one, reports a
        // broken or timed-out connection as a transient exception without a SQLSTATE.
        return databaseException.IsTransient;
    }

    private static bool _HasTransportFault(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is IOException or SocketException or TimeoutException)
            {
                return true;
            }
        }

        return false;
    }

    private static byte? _GetSqlClientSeverity(DbException exception)
    {
        return exception.GetType().GetProperty("Class")?.GetValue(exception) as byte?;
    }
}
