// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections;
using System.Collections.Concurrent;
using System.Data.Common;
using System.Reflection;
using System.Text.RegularExpressions;
using Headless.Checks;

namespace Headless;

/// <summary>
/// Reads a relational failure without referencing any ADO.NET driver: finds the database exception in a failure's
/// chain, enumerates every SQL Server error number it carries, and recognizes a unique-key violation on PostgreSQL,
/// SQL Server, and SQLite, with the violated constraint's name where the engine reports one. The unit of work's transient classifier reads faults the same way; build a narrower
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
public static partial class RelationalFaults
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
    private static readonly ConcurrentDictionary<Type, PropertyInfo?> _ConstraintNameProperties = new();
    private static readonly ConcurrentDictionary<Type, PropertyInfo?> _MessageProperties = new();

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

    /// <summary>
    /// Returns whether <paramref name="exception" /> reports a unique-key or primary-key violation, as
    /// <see cref="IsUniqueViolation(Exception)" /> does, and the name of the violated constraint or unique index when
    /// the engine reports it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Use it to answer a lost uniqueness race with your own conflict: a check that no other row holds a value passes,
    /// a concurrent writer takes the value, and the save fails on the index that enforces it. Compare the name
    /// ordinally with the name the database holds, such as EF Core's <c>GetDatabaseName()</c> for the index.
    /// </para>
    /// <para>
    /// PostgreSQL reports the name in its own field, read from the driver's <c>ConstraintName</c> (Npgsql's
    /// <c>PostgresException</c>). SQL Server reports it only inside the message text of error 2627 (a primary-key or
    /// unique constraint) or 2601 (a unique index), so the name is read from the English message and is
    /// <see langword="null" /> when the session's language (<c>SET LANGUAGE</c>, or the login's default language)
    /// localizes the message. SQLite reports the columns, not a name, so the name is always
    /// <see langword="null" /> there.
    /// </para>
    /// </remarks>
    /// <param name="exception">The failure, as thrown, or the database exception itself.</param>
    /// <param name="constraintName">
    /// The violated constraint or unique index; <see langword="null" /> when the result is <see langword="false" /> or
    /// the engine did not report a name.
    /// </param>
    /// <returns><see langword="true" /> when the statement failed on a duplicate key.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="exception" /> is <see langword="null" />.</exception>
    public static bool IsUniqueViolation(Exception exception, out string? constraintName)
    {
        Argument.IsNotNull(exception);

        constraintName = null;

        if (FindDatabaseException(exception) is not { } databaseException || !_IsUniqueViolation(databaseException))
        {
            return false;
        }

        constraintName = _ReadUniqueConstraintName(databaseException);

        return true;
    }

    /// <summary>
    /// Recognizes serialization failures, deadlock victims, and unique-key violations that can be retried after
    /// disposing the failed transaction. Includes SQLite busy and locked results, but not connection failures or timeouts.
    /// </summary>
    /// <remarks>
    /// A PostgreSQL or SQL Server conflict remains a rejected write when reported by commit. Re-read the protected
    /// representation before replaying it. A false result does not establish whether a commit succeeded.
    /// </remarks>
    /// <param name="exception">The failure as thrown, including an EF wrapper.</param>
    /// <returns>Whether the failure is a known concurrency conflict, rather than an uncertain commit reply.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="exception" /> is <see langword="null" />.</exception>
    public static bool IsConcurrencyConflict(Exception exception)
    {
        Argument.IsNotNull(exception);
        var fault = FindDatabaseException(exception);
        if (fault is null)
        {
            return false;
        }

        if (
            _IsUniqueViolation(fault)
            || fault.SqlState
                is SqlErrorCodes.PostgreSql.SerializationFailure
                    or SqlErrorCodes.PostgreSql.DeadlockDetected
        )
        {
            return true;
        }

        if (
            GetSqlServerErrorNumbers(fault)
                .Any(number =>
                    number
                        is SqlErrorCodes.SqlServer.DeadlockVictim
                            or SqlErrorCodes.SqlServer.SnapshotUpdateConflict
                            or SqlErrorCodes.SqlServer.SnapshotMetadataConflict
                )
        )
        {
            return true;
        }

        if (string.Equals(fault.GetType().FullName, _SqliteExceptionTypeName, StringComparison.Ordinal))
        {
            var property = _SqliteExtendedErrorCodeProperties.GetOrAdd(
                fault.GetType(),
                static type => type.GetProperty("SqliteExtendedErrorCode", BindingFlags.Public | BindingFlags.Instance)
            );
            return property?.GetValue(fault) is int code && (code & 0xff) is 5 or 6;
        }

        return false;
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

    private static string? _ReadUniqueConstraintName(DbException exception)
    {
        if (string.Equals(exception.SqlState, SqlErrorCodes.PostgreSql.UniqueViolation, StringComparison.Ordinal))
        {
            return
                _ConstraintNameProperties
                    .GetOrAdd(
                        exception.GetType(),
                        static type => type.GetProperty("ConstraintName", BindingFlags.Public | BindingFlags.Instance)
                    )
                    ?.GetValue(exception) as string
                    is { Length: > 0 } name
                ? name
                : null;
        }

        if (!_IsSqlClientException(exception))
        {
            return null;
        }

        // The duplicate key is not always the first error, so the message is read from the error that reported it;
        // the legacy driver exposes no collection, and its own message is that of the first error.
        if (
            _ErrorsProperties
                .GetOrAdd(exception.GetType(), static type => type.GetProperty("Errors"))
                ?.GetValue(exception)
            is IEnumerable errors
        )
        {
            foreach (var error in errors)
            {
                if (error is not null && _ReadNumber(error) is { } number && _ReadMessage(error) is { } message)
                {
                    if (_ParseSqlServerConstraintName(number, message) is { } name)
                    {
                        return name;
                    }
                }
            }

            return null;
        }

        return _ReadNumber(exception) is { } own ? _ParseSqlServerConstraintName(own, exception.Message) : null;
    }

    private static string? _ParseSqlServerConstraintName(int number, string message)
    {
        var match = number switch
        {
            SqlErrorCodes.SqlServer.DuplicateKeyUniqueConstraint => _SqlServerDuplicateConstraint.Match(message),
            SqlErrorCodes.SqlServer.DuplicateKeyUniqueIndex => _SqlServerDuplicateIndex.Match(message),
            _ => null,
        };

        return match is { Success: true } ? match.Groups["name"].Value : null;
    }

    private static string? _ReadMessage(object error)
    {
        return _MessageProperties.GetOrAdd(error.GetType(), static type => type.GetProperty("Message"))?.GetValue(error)
            as string;
    }

    // Error 2627: "Violation of PRIMARY KEY constraint 'PK_T'. Cannot insert duplicate key in object 'dbo.T'. ..."
    // (UNIQUE KEY for a unique constraint). The lazy name stops at the first "'. Cannot insert", so a name holding a
    // quote still parses.
    [GeneratedRegex(
        pattern: @"\Aviolation of [a-z ]+ constraint '(?<name>.+?)'\. cannot insert duplicate key",
        options: RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: RegexPatterns.MatchTimeoutMilliseconds
    )]
    private static partial Regex _SqlServerDuplicateConstraint { get; }

    // Error 2601: "Cannot insert duplicate key row in object 'dbo.T' with unique index 'IX_T_Email'. ..."
    [GeneratedRegex(
        pattern: @"\Acannot insert duplicate key row in object '.+?' with unique index '(?<name>.+?)'\.(?: |\z)",
        options: RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: RegexPatterns.MatchTimeoutMilliseconds
    )]
    private static partial Regex _SqlServerDuplicateIndex { get; }

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
