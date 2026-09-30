// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;

namespace Headless.UnitOfWork.Internal;

/// <summary>
/// Classifies a relational failure as transient: a fault that a replay of the whole transaction, on a fresh
/// transaction, may cure. It is the default replay filter for the unit of work and the base that narrower
/// classifiers (the Jobs tree delete) add their own conflicts to. The rules that belong to one database live in
/// <see cref="PostgreSqlTransientFaults" /> and <see cref="SqlServerTransientFaults" />; this type owns what is
/// shared and composes them.
/// </summary>
/// <remarks>
/// <para>
/// A cancellation is never transient, including a driver that reports a cancel as a database exception while the
/// caller token is already cancelled: a replay would only be cancelled again.
/// </para>
/// <para>
/// The walk is outer-first and stops at the FIRST <see cref="DbException" />: drivers report a dropped connection
/// as a transient <see cref="DbException" /> wrapping the underlying <see cref="IOException" /> or socket error,
/// so walking innermost-first would reach the socket error and lose the transient signal on the driver exception.
/// </para>
/// <para>
/// <see cref="DbException.IsTransient" /> is only one signal. Npgsql and MySqlConnector override it, so their
/// connection, capacity, and lock faults arrive classified. SQL Server's <c>SqlException</c> overrides neither it
/// nor <see cref="DbException.SqlState" />, which is why <see cref="SqlServerTransientFaults" /> matches on error
/// numbers instead.
/// </para>
/// <para>
/// The commit phase is not this classifier's concern: whoever replays must refuse to replay a commit, which may
/// have succeeded on the server before it failed on the wire, whatever this classifier says about its fault.
/// </para>
/// </remarks>
internal static class RelationalTransientFaults
{
    /// <summary>Returns whether <paramref name="exception" /> is a transient relational failure.</summary>
    /// <param name="exception">The failure, as thrown; wrappers such as EF's <c>DbUpdateException</c> are walked.</param>
    /// <param name="cancellationToken">The caller's token; a failure observed after it was cancelled is never transient.</param>
    public static bool IsTransient(Exception exception, CancellationToken cancellationToken)
    {
        if (IsCancellation(exception, cancellationToken))
        {
            return false;
        }

        if (FindDatabaseException(exception) is not { } databaseException)
        {
            return false;
        }

        return databaseException.IsTransient
            || PostgreSqlTransientFaults.IsTransientSqlState(databaseException.SqlState)
            || (
                SqlServerTransientFaults.IsSqlClientException(databaseException)
                && SqlServerTransientFaults.HasTransientError(databaseException)
            );
    }

    /// <summary>Whether the failure is, or was observed during, a cancellation.</summary>
    public static bool IsCancellation(Exception exception, CancellationToken cancellationToken)
    {
        return cancellationToken.IsCancellationRequested || exception is OperationCanceledException;
    }

    /// <summary>The outermost <see cref="DbException" /> in <paramref name="exception" />'s inner-exception chain.</summary>
    public static DbException? FindDatabaseException(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbException databaseException)
            {
                return databaseException;
            }
        }

        return null;
    }
}
