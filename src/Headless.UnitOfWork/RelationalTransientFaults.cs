// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using System.Data.Common;
using Headless.Checks;
using Headless.UnitOfWork.Internal;

namespace Headless.UnitOfWork;

/// <summary>
/// Classifies a relational failure as transient: a fault that a replay of the whole transaction, on a fresh
/// transaction, may cure. It is the framework's default replay filter (the unit-of-work runner, the SQL store kit's
/// autonomous calls, the Jobs claim scopes) and the composition point a narrower
/// classifier builds on (the Jobs tree delete adds its own foreign-key conflicts); reuse or compose it in a
/// hand-rolled retry loop around <c>RunAsync</c> so a custom loop keeps the framework's classification.
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
/// nor <see cref="DbException.SqlState" />, which is why the SQL Server half matches on error numbers instead,
/// over every error the exception carries.
/// </para>
/// <para>
/// The commit phase is not this classifier's concern: whoever replays must refuse to replay a commit, which may
/// have succeeded on the server before it failed on the wire, whatever this classifier says about its fault.
/// </para>
/// </remarks>
[PublicAPI]
public static class RelationalTransientFaults
{
    /// <summary>Returns whether <paramref name="exception" /> is a transient relational failure.</summary>
    /// <param name="exception">The failure, as thrown; wrappers such as EF's <c>DbUpdateException</c> are walked.</param>
    /// <param name="cancellationToken">The caller's token; a failure observed after it was cancelled is never transient.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="exception" /> is <see langword="null" />.
    /// </exception>
    public static bool IsTransient(Exception exception, CancellationToken cancellationToken)
    {
        Argument.IsNotNull(exception);

        if (TransientFaults.IsCancellation(exception, cancellationToken))
        {
            return false;
        }

        if (TransientFaults.FindDatabaseException(exception) is not { } databaseException)
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
}
