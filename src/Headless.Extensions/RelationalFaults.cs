// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections;
using System.Collections.Concurrent;
using System.Data.Common;
using System.Reflection;
using Headless.Checks;

namespace Headless;

/// <summary>
/// Reads a relational failure without referencing any ADO.NET driver: finds the database exception in a failure's
/// chain, enumerates every SQL Server error number it carries, and recognizes a unique-key violation on PostgreSQL,
/// SQL Server, and SQLite. The unit of work's transient classifier reads faults the same way; build a narrower
/// classifier (a conflict mapper, a duplicate-insert check) on these instead of reading driver properties by hand.
/// </summary>
/// <remarks>
/// <para>
/// A provider's codes are read only from that provider's exception type, recognized by its full name, because the
/// same number means something else on another driver: MySQL reports a lock-wait timeout as 1205, SQL Server's
/// deadlock victim. PostgreSQL is the exception: its SQLSTATE travels through the standard
/// <see cref="DbException.SqlState" />, so any driver that reports SQLSTATE is read the same way.
/// </para>
/// <para>
/// SqlClient reports several errors per <c>SqlException</c>, and its <c>Number</c> is only the first. A duplicate key
/// or a deadlock is not always reported first, so every check here reads every error.
/// </para>
/// </remarks>
[PublicAPI]
public static class RelationalFaults
{
    private const string _SqlClientExceptionTypeName = "Microsoft.Data.SqlClient.SqlException";
    private const string _LegacySqlClientExceptionTypeName = "System.Data.SqlClient.SqlException";
    private const string _SqliteExceptionTypeName = "Microsoft.Data.Sqlite.SqliteException";

    // SQLite extended result codes from sqlite3.h: SQLITE_CONSTRAINT (19) qualified by its unique (8) and primary-key
    // (6) subcodes. The primary code alone also covers NOT NULL, CHECK, and foreign-key failures, so it cannot be used.
    private const int _SqliteConstraintUnique = 19 | (8 << 8);
    private const int _SqliteConstraintPrimaryKey = 19 | (6 << 8);

    // Reflection runs only on the fault path, but a fault under load is exactly when a per-call GetProperty would
    // hurt, so the lookups are cached per exception (and per error) type.
    private static readonly ConcurrentDictionary<Type, PropertyInfo?> _ErrorsProperties = new();
    private static readonly ConcurrentDictionary<Type, PropertyInfo?> _NumberProperties = new();
    private static readonly ConcurrentDictionary<Type, PropertyInfo?> _SqliteExtendedErrorCodeProperties = new();

    /// <summary>
    /// Returns the outermost <see cref="DbException" /> in <paramref name="exception" />'s inner-exception chain,
    /// <paramref name="exception" /> itself included.
    /// </summary>
    /// <remarks>
    /// The walk is outer-first on purpose: a driver reports a dropped connection as a <see cref="DbException" /> that
    /// wraps the underlying <see cref="IOException" /> or socket error, so <see cref="Exception.GetBaseException" />
    /// would skip the exception that carries the provider's error code.
    /// </remarks>
    /// <param name="exception">The failure, as thrown; wrappers such as EF's <c>DbUpdateException</c> are walked.</param>
    /// <returns>The database exception, or <see langword="null" /> when the chain carries none.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="exception" /> is <see langword="null" />.</exception>
    public static DbException? FindDatabaseException(Exception exception)
    {
        Argument.IsNotNull(exception);

        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbException databaseException)
            {
                return databaseException;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns every SQL Server error number <paramref name="exception" /> carries, in the order the server reported
    /// them: the <c>Number</c> of each item in <c>SqlException.Errors</c>, or the exception's own <c>Number</c> when
    /// it exposes no collection.
    /// </summary>
    /// <param name="exception">The database exception to read.</param>
    /// <returns>
    /// The error numbers; empty when <paramref name="exception" /> is not SqlClient's <c>SqlException</c>
    /// (<c>Microsoft.Data.SqlClient</c> or <c>System.Data.SqlClient</c>), whose numbers mean nothing on another
    /// driver.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="exception" /> is <see langword="null" />.</exception>
    public static IReadOnlyList<int> GetSqlServerErrorNumbers(DbException exception)
    {
        Argument.IsNotNull(exception);

        return _IsSqlClientException(exception) ? _ReadErrorNumbers(exception) : [];
    }

    /// <summary>
    /// Returns whether <paramref name="exception" /> reports a unique-key or primary-key violation: SQLSTATE
    /// <c>23505</c> on PostgreSQL (or any driver that reports SQLSTATE), error 2627 or 2601 in any error of a SQL
    /// Server <c>SqlException</c>, or <c>SQLITE_CONSTRAINT_UNIQUE</c> (2067) or <c>SQLITE_CONSTRAINT_PRIMARYKEY</c>
    /// (1555) on <c>Microsoft.Data.Sqlite</c>.
    /// </summary>
    /// <remarks>
    /// The database exception is found with <see cref="FindDatabaseException" />, so a failure can be passed as thrown,
    /// such as EF Core's <c>DbUpdateException</c> in an exception filter.
    /// </remarks>
    /// <param name="exception">The failure, as thrown, or the database exception itself.</param>
    /// <returns><see langword="true" /> when the statement failed on a duplicate key.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="exception" /> is <see langword="null" />.</exception>
    public static bool IsUniqueViolation(Exception exception)
    {
        Argument.IsNotNull(exception);

        return FindDatabaseException(exception) is { } databaseException && _IsUniqueViolation(databaseException);
    }

    private static bool _IsUniqueViolation(DbException exception)
    {
        if (string.Equals(exception.SqlState, SqlErrorCodes.PostgreSql.UniqueViolation, StringComparison.Ordinal))
        {
            return true;
        }

        if (_IsSqlClientException(exception))
        {
            foreach (var number in _ReadErrorNumbers(exception))
            {
                if (
                    number
                    is SqlErrorCodes.SqlServer.DuplicateKeyUniqueConstraint
                        or SqlErrorCodes.SqlServer.DuplicateKeyUniqueIndex
                )
                {
                    return true;
                }
            }

            return false;
        }

        if (string.Equals(exception.GetType().FullName, _SqliteExceptionTypeName, StringComparison.Ordinal))
        {
            var property = _SqliteExtendedErrorCodeProperties.GetOrAdd(
                exception.GetType(),
                static type => type.GetProperty("SqliteExtendedErrorCode", BindingFlags.Public | BindingFlags.Instance)
            );

            return property?.GetValue(exception) is _SqliteConstraintUnique or _SqliteConstraintPrimaryKey;
        }

        return false;
    }

    private static bool _IsSqlClientException(DbException exception)
    {
        return exception.GetType().FullName is _SqlClientExceptionTypeName or _LegacySqlClientExceptionTypeName;
    }

    private static List<int> _ReadErrorNumbers(DbException exception)
    {
        var numbers = new List<int>();

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
                    numbers.Add(number);
                }
            }
        }

        if (numbers.Count == 0 && _ReadNumber(exception) is { } own)
        {
            numbers.Add(own);
        }

        return numbers;
    }

    private static int? _ReadNumber(object source)
    {
        return _NumberProperties.GetOrAdd(source.GetType(), static type => type.GetProperty("Number"))?.GetValue(source)
            as int?;
    }
}
