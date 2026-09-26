// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;

namespace Headless.UnitOfWork.Internal;

/// <summary>
/// Classifies a relational failure as transient: a fault that a replay of the whole transaction, on a fresh
/// transaction, may cure. It is the default replay filter for the unit of work and the base that narrower
/// classifiers (the Jobs tree delete) add their own conflicts to.
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
/// <see cref="DbException.IsTransient" /> is only one signal. SQL Server's <c>SqlException</c> overrides neither it
/// nor <see cref="DbException.SqlState" />, so its deadlock and snapshot conflicts are matched on the error number,
/// read by reflection because this package references no driver. Serialization failures (SQLSTATE 40001, SQL
/// Server 3960) stay in the set, so replay keeps working when a consumer raises the isolation level.
/// </para>
/// <para>
/// The commit phase is not this classifier's concern: whoever replays must refuse to replay a commit, which may
/// have succeeded on the server before it failed on the wire, whatever this classifier says about its fault.
/// </para>
/// </remarks>
internal static class RelationalTransientFaults
{
    private const string _SerializationFailureSqlState = "40001";
    private const string _PostgreSqlDeadlockDetectedSqlState = "40P01";
    private const int _SqlServerDeadlockVictim = 1205;
    private const int _SqlServerSnapshotUpdateConflict = 3960;
    private const string _SqlClientExceptionTypeName = "Microsoft.Data.SqlClient.SqlException";

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
            || IsTransientSqlState(databaseException.SqlState)
            || (
                string.Equals(
                    databaseException.GetType().FullName,
                    _SqlClientExceptionTypeName,
                    StringComparison.Ordinal
                ) && IsTransientSqlServerNumber(GetErrorNumber(databaseException))
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

    /// <summary>Whether a SQLSTATE reports a serialization failure or a deadlock, both cured by a replay.</summary>
    public static bool IsTransientSqlState(string? sqlState)
    {
        return sqlState is _SerializationFailureSqlState or _PostgreSqlDeadlockDetectedSqlState;
    }

    /// <summary>Whether a SQL Server error number reports a deadlock or a snapshot update conflict.</summary>
    public static bool IsTransientSqlServerNumber(int? number)
    {
        return number is _SqlServerDeadlockVictim or _SqlServerSnapshotUpdateConflict;
    }

    /// <summary>
    /// The driver's error number, read from a public <c>Number</c> property because this package references no
    /// driver assembly; <see langword="null" /> when the exception exposes none.
    /// </summary>
    public static int? GetErrorNumber(DbException exception)
    {
        return exception.GetType().GetProperty("Number")?.GetValue(exception) as int?;
    }
}
