// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Data.Common;
using System.Reflection;

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
/// <see cref="DbException.IsTransient" /> is only one signal. Npgsql and MySqlConnector override it, so their
/// connection, capacity, and lock faults arrive classified. SQL Server's <c>SqlException</c> overrides neither it
/// nor <see cref="DbException.SqlState" />, so its faults are matched on the error numbers, read by reflection
/// because this package references no driver, over every error the exception carries: <c>Number</c> alone is the
/// first error, and a deadlock or a connection fault is not always reported first. Serialization failures
/// (SQLSTATE 40001, SQL Server 3960) stay in the set, so replay keeps working when a consumer raises the
/// isolation level.
/// </para>
/// <para>
/// The commit phase is not this classifier's concern: whoever replays must refuse to replay a commit, which may
/// have succeeded on the server before it failed on the wire, whatever this classifier says about its fault.
/// </para>
/// </remarks>
internal static class RelationalTransientFaults
{
    // The same values as SqlErrorCodes.PostgreSql in Headless.Extensions, which this package cannot reference
    // without taking on Polly, Humanizer, MoreLinq, and System.Reactive; keep the two in step by hand.
    private const string _SerializationFailureSqlState = "40001";
    private const string _PostgreSqlDeadlockDetectedSqlState = "40P01";
    private const string _SqlClientExceptionTypeName = "Microsoft.Data.SqlClient.SqlException";

    /// <summary>
    /// The SQL Server error numbers a replay may cure: the deadlock victim (1205) and the snapshot update conflict
    /// (3960) the tree delete has always retried, and the transient set EF Core's
    /// <c>SqlServerTransientExceptionDetector</c> replays under <c>EnableRetryOnFailure</c> (release/10.0, the
    /// branch of the pinned <c>Microsoft.EntityFrameworkCore.SqlServer</c>), so an EF block and a raw-ADO block
    /// replay the same faults on the same database. Two of EF's entries are deliberately absent: the client-side
    /// command timeout (-2) can fire after the statement completed on the server, and 203 is transient only when
    /// a <c>Win32Exception</c> sits beneath it, which a driver-free read cannot see.
    /// </summary>
    private static readonly FrozenSet<int> _SqlServerTransientNumbers = FrozenSet.ToFrozenSet([
        20,
        64,
        121,
        233,
        539,
        601,
        615,
        617,
        669,
        921,
        926,
        927,
        941,
        952,
        982,
        988,
        997,
        1203,
        1204,
        1205,
        1215,
        1216,
        1221,
        1222,
        1232,
        1404,
        1413,
        1421,
        1438,
        1532,
        1533,
        1534,
        1535,
        1807,
        2021,
        2816,
        3429,
        3635,
        3935,
        3941,
        3947,
        3948,
        3950,
        3953,
        3957,
        3960,
        3966,
        3980,
        4060,
        4117,
        4184,
        4221,
        5280,
        5529,
        6292,
        7951,
        8628,
        8645,
        8651,
        9020,
        9515,
        9985,
        10053,
        10054,
        10060,
        10922,
        10928,
        10929,
        10930,
        10936,
        11001,
        11539,
        14355,
        14817,
        14868,
        14906,
        16528,
        16554,
        16555,
        17065,
        17066,
        17067,
        17197,
        17889,
        18401,
        18858,
        19413,
        19416,
        19494,
        20041,
        21503,
        22225,
        22226,
        22335,
        22353,
        22355,
        22358,
        22427,
        22430,
        22491,
        22493,
        22498,
        22754,
        22758,
        22759,
        22760,
        22984,
        25003,
        25738,
        25740,
        30080,
        30085,
        33123,
        35216,
        35218,
        35256,
        35293,
        37202,
        37327,
        39025,
        39108,
        39110,
        39151,
        39152,
        40106,
        40143,
        40189,
        40197,
        40501,
        40540,
        40613,
        40623,
        40642,
        40648,
        40671,
        40675,
        40890,
        40903,
        40918,
        40938,
        41301,
        41302,
        41305,
        41325,
        41339,
        41383,
        41614,
        41619,
        41640,
        41700,
        41701,
        41823,
        41839,
        41840,
        42029,
        42108,
        42109,
        45153,
        45156,
        45157,
        45161,
        45179,
        45182,
        45319,
        45547,
        47132,
        47137,
        47139,
        49510,
        49802,
        49918,
        49919,
        49920,
        49977,
        49983,
    ]);

    // Reflection runs only on the fault path, but a fault under load is exactly when a per-call GetProperty
    // would hurt, so the lookups are cached per exception (and per error) type.
    private static readonly ConcurrentDictionary<Type, PropertyInfo?> _ErrorsProperties = new();
    private static readonly ConcurrentDictionary<Type, PropertyInfo?> _NumberProperties = new();

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
            || (IsSqlClientException(databaseException) && HasTransientSqlServerError(databaseException));
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

    /// <summary>
    /// Whether <paramref name="exception" /> is SqlClient's exception, matched by type name because this package
    /// references no driver. A SQL Server error number means nothing on another driver: MySQL reports a lock-wait
    /// timeout as 1205.
    /// </summary>
    public static bool IsSqlClientException(DbException exception)
    {
        return string.Equals(exception.GetType().FullName, _SqlClientExceptionTypeName, StringComparison.Ordinal);
    }

    /// <summary>Whether a SQL Server error number is one a replay may cure.</summary>
    public static bool IsTransientSqlServerNumber(int? number)
    {
        return number is { } value && _SqlServerTransientNumbers.Contains(value);
    }

    /// <summary>Whether any error <paramref name="exception" /> carries is a SQL Server error a replay may cure.</summary>
    public static bool HasTransientSqlServerError(DbException exception)
    {
        foreach (var number in GetErrorNumbers(exception))
        {
            if (_SqlServerTransientNumbers.Contains(number))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The driver's error number, read from a public <c>Number</c> property because this package references no
    /// driver assembly; <see langword="null" /> when the exception exposes none. On SqlClient this is the FIRST
    /// error only; classify with <see cref="GetErrorNumbers" /> when any error in the batch counts.
    /// </summary>
    public static int? GetErrorNumber(DbException exception)
    {
        return _ReadNumber(exception);
    }

    /// <summary>
    /// Every error number <paramref name="exception" /> carries: the <c>Number</c> of each item in a public
    /// <c>Errors</c> collection (SqlClient reports several errors per exception, and <c>Number</c> is only the
    /// first), else the exception's own <c>Number</c>; empty when it exposes neither.
    /// </summary>
    public static IEnumerable<int> GetErrorNumbers(DbException exception)
    {
        var any = false;

        if (
            _ErrorsProperties
                .GetOrAdd(exception.GetType(), static type => type.GetProperty("Errors"))
                ?.GetValue(exception)
            is IEnumerable errors
        )
        {
            foreach (var error in errors)
            {
                if (error is not null && _ReadNumber(error) is { } number)
                {
                    any = true;

                    yield return number;
                }
            }
        }

        if (!any && _ReadNumber(exception) is { } own)
        {
            yield return own;
        }
    }

    private static int? _ReadNumber(object source)
    {
        return _NumberProperties.GetOrAdd(source.GetType(), static type => type.GetProperty("Number"))?.GetValue(source)
            as int?;
    }
}
