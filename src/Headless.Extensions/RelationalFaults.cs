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
/// chain, enumerates every SQL Server error number it carries, and recognizes a unique-key, foreign-key, CHECK, or NOT
/// NULL violation, a value too long for its column, and a numeric overflow on PostgreSQL, SQL Server, and SQLite, with
/// the violated constraint's name where the engine reports one. The unit of work's transient classifier reads faults the same way; build a narrower
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
    private const int _SqliteConstraintCheck = 19 | (1 << 8);
    private const int _SqliteConstraintForeignKey = 19 | (3 << 8);
    private const int _SqliteConstraintNotNull = 19 | (5 << 8);
    private const int _SqliteConstraintTrigger = 19 | (7 << 8);
    private const int _SqliteTooBig = 18;

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
    /// Returns whether <paramref name="exception" /> reports a foreign-key violation: a write that names a missing
    /// row, or a delete or update refused because other rows still reference the row.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PostgreSQL reports SQLSTATE <c>23503</c> for a missing row and for a <c>NO ACTION</c> key, and <c>23001</c> for a
    /// <c>RESTRICT</c> key (EF Core's <c>DeleteBehavior.Restrict</c>), so both count. SQL Server reports error 547 for
    /// a foreign key and for a CHECK constraint alike; the English message tells them apart, and a 547 whose message the
    /// session's language localizes counts as a foreign-key violation. SQLite reports
    /// <c>SQLITE_CONSTRAINT_FOREIGNKEY</c> (787), or <c>SQLITE_CONSTRAINT_TRIGGER</c> (1811) with the message
    /// "FOREIGN KEY constraint failed" for a <c>RESTRICT</c> key, only while <c>PRAGMA foreign_keys</c> is on.
    /// </para>
    /// <para>
    /// Use it to answer a delete of a row that is still in use with your own conflict, matching the key by name with
    /// <see cref="IsReferenceViolation(Exception, out string?)" />.
    /// </para>
    /// </remarks>
    /// <param name="exception">The failure, as thrown, or the database exception itself.</param>
    /// <returns><see langword="true" /> when the statement failed on a foreign key.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="exception" /> is <see langword="null" />.</exception>
    public static bool IsReferenceViolation(Exception exception)
    {
        return IsReferenceViolation(exception, out _);
    }

    /// <summary>
    /// Returns whether <paramref name="exception" /> reports a foreign-key violation, as
    /// <see cref="IsReferenceViolation(Exception)" /> does, and the name of the violated key when the engine reports it.
    /// </summary>
    /// <remarks>
    /// PostgreSQL reports the name in its own field (Npgsql's <c>ConstraintName</c>). SQL Server reports it only in the
    /// English message of error 547, so the name is <see langword="null" /> when the session's language localizes the
    /// message. SQLite reports no name.
    /// </remarks>
    /// <param name="exception">The failure, as thrown, or the database exception itself.</param>
    /// <param name="constraintName">
    /// The violated foreign key; <see langword="null" /> when the result is <see langword="false" /> or the engine did
    /// not report a name.
    /// </param>
    /// <returns><see langword="true" /> when the statement failed on a foreign key.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="exception" /> is <see langword="null" />.</exception>
    public static bool IsReferenceViolation(Exception exception, out string? constraintName)
    {
        Argument.IsNotNull(exception);

        constraintName = null;

        if (FindDatabaseException(exception) is not { } databaseException)
        {
            return false;
        }

        if (
            databaseException.SqlState
            is SqlErrorCodes.PostgreSql.ForeignKeyViolation
                or SqlErrorCodes.PostgreSql.RestrictViolation
        )
        {
            constraintName = _ReadPostgreSqlConstraintName(databaseException);

            return true;
        }

        if (_FindSqlServerConstraintConflict(databaseException) is { IsCheck: false } conflict)
        {
            constraintName = conflict.Name;

            return true;
        }

        return _ReadSqliteExtendedErrorCode(databaseException) switch
        {
            _SqliteConstraintForeignKey => true,
            // SQLite enforces a RESTRICT action through an internal trigger, so the refusal carries the trigger
            // subcode; its fixed English message (SQLite never localizes one) tells it from a user trigger's RAISE.
            _SqliteConstraintTrigger => databaseException.Message.Contains(
                "FOREIGN KEY constraint failed",
                StringComparison.Ordinal
            ),
            _ => false,
        };
    }

    /// <summary>
    /// Returns whether <paramref name="exception" /> reports a CHECK constraint violation: SQLSTATE <c>23514</c> on
    /// PostgreSQL, error 547 naming a CHECK constraint in SQL Server's English message, or
    /// <c>SQLITE_CONSTRAINT_CHECK</c> (275) on <c>Microsoft.Data.Sqlite</c>.
    /// </summary>
    /// <remarks>
    /// SQL Server shares error 547 with foreign keys, so a CHECK violation whose message the session's language
    /// localizes is not recognized here; <see cref="IsReferenceViolation(Exception)" /> reports it instead.
    /// </remarks>
    /// <param name="exception">The failure, as thrown, or the database exception itself.</param>
    /// <returns><see langword="true" /> when the statement failed on a CHECK constraint.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="exception" /> is <see langword="null" />.</exception>
    public static bool IsCheckViolation(Exception exception)
    {
        return IsCheckViolation(exception, out _);
    }

    /// <summary>
    /// Returns whether <paramref name="exception" /> reports a CHECK constraint violation, as
    /// <see cref="IsCheckViolation(Exception)" /> does, and the constraint's name when the engine reports it.
    /// </summary>
    /// <remarks>
    /// PostgreSQL reports the name in its own field and SQL Server in the English message. SQLite reports it only in its
    /// message text, so the name is <see langword="null" /> there.
    /// </remarks>
    /// <param name="exception">The failure, as thrown, or the database exception itself.</param>
    /// <param name="constraintName">
    /// The violated CHECK constraint; <see langword="null" /> when the result is <see langword="false" /> or the engine
    /// did not report a name.
    /// </param>
    /// <returns><see langword="true" /> when the statement failed on a CHECK constraint.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="exception" /> is <see langword="null" />.</exception>
    public static bool IsCheckViolation(Exception exception, out string? constraintName)
    {
        Argument.IsNotNull(exception);

        constraintName = null;

        if (FindDatabaseException(exception) is not { } databaseException)
        {
            return false;
        }

        if (
            string.Equals(databaseException.SqlState, SqlErrorCodes.PostgreSql.CheckViolation, StringComparison.Ordinal)
        )
        {
            constraintName = _ReadPostgreSqlConstraintName(databaseException);

            return true;
        }

        if (_FindSqlServerConstraintConflict(databaseException) is { IsCheck: true } conflict)
        {
            constraintName = conflict.Name;

            return true;
        }

        return _ReadSqliteExtendedErrorCode(databaseException) is _SqliteConstraintCheck;
    }

    /// <summary>
    /// Returns whether <paramref name="exception" /> reports a NULL written to a column that does not allow one: SQLSTATE
    /// <c>23502</c> on PostgreSQL, error 515 in any error of a SQL Server <c>SqlException</c>, or
    /// <c>SQLITE_CONSTRAINT_NOTNULL</c> (1299) on <c>Microsoft.Data.Sqlite</c>.
    /// </summary>
    /// <param name="exception">The failure, as thrown, or the database exception itself.</param>
    /// <returns><see langword="true" /> when the statement failed on a NOT NULL column.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="exception" /> is <see langword="null" />.</exception>
    public static bool IsNotNullViolation(Exception exception)
    {
        Argument.IsNotNull(exception);

        return FindDatabaseException(exception) is { } databaseException
            && (
                string.Equals(
                    databaseException.SqlState,
                    SqlErrorCodes.PostgreSql.NotNullViolation,
                    StringComparison.Ordinal
                )
                || GetSqlServerErrorNumbers(databaseException).Contains(SqlErrorCodes.SqlServer.CannotInsertNull)
                || _ReadSqliteExtendedErrorCode(databaseException) is _SqliteConstraintNotNull
            );
    }

    /// <summary>
    /// Returns whether <paramref name="exception" /> reports a value longer than its column allows: SQLSTATE
    /// <c>22001</c> on PostgreSQL, or error 8152 or 2628 in any error of a SQL Server <c>SqlException</c>.
    /// </summary>
    /// <remarks>
    /// SQLite does not enforce a declared length such as <c>varchar(64)</c>, so it never reports one; its
    /// <c>SQLITE_TOOBIG</c> (18) for a value past the engine's own size limit counts.
    /// </remarks>
    /// <param name="exception">The failure, as thrown, or the database exception itself.</param>
    /// <returns><see langword="true" /> when the statement failed on a value's length.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="exception" /> is <see langword="null" />.</exception>
    public static bool IsMaxLengthExceeded(Exception exception)
    {
        Argument.IsNotNull(exception);

        return FindDatabaseException(exception) is { } databaseException
            && (
                string.Equals(
                    databaseException.SqlState,
                    SqlErrorCodes.PostgreSql.StringDataRightTruncation,
                    StringComparison.Ordinal
                )
                || GetSqlServerErrorNumbers(databaseException)
                    .Any(number =>
                        number
                            is SqlErrorCodes.SqlServer.StringOrBinaryDataTruncated
                                or SqlErrorCodes.SqlServer.StringOrBinaryDataTruncatedInColumn
                    )
                || _ReadSqliteExtendedErrorCode(databaseException) is _SqliteTooBig
            );
    }

    /// <summary>
    /// Returns whether <paramref name="exception" /> reports a number outside its column's range or precision: SQLSTATE
    /// <c>22003</c> on PostgreSQL, or error 8115 or 220 in any error of a SQL Server <c>SqlException</c>.
    /// </summary>
    /// <remarks>
    /// SQLite stores any number in any numeric column, so it never reports one. SqlClient refuses a <see langword="decimal" />
    /// parameter beyond the precision it declares with an <see cref="ArgumentException" /> before the server sees it,
    /// such as an EF Core save to a <c>decimal(5, 2)</c> column; that failure carries no database error, so this
    /// returns <see langword="false" /> for it.
    /// </remarks>
    /// <param name="exception">The failure, as thrown, or the database exception itself.</param>
    /// <returns><see langword="true" /> when the statement failed on a numeric overflow.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="exception" /> is <see langword="null" />.</exception>
    public static bool IsNumericOverflow(Exception exception)
    {
        Argument.IsNotNull(exception);

        return FindDatabaseException(exception) is { } databaseException
            && (
                string.Equals(
                    databaseException.SqlState,
                    SqlErrorCodes.PostgreSql.NumericValueOutOfRange,
                    StringComparison.Ordinal
                )
                || GetSqlServerErrorNumbers(databaseException)
                    .Any(number =>
                        number
                            is SqlErrorCodes.SqlServer.ArithmeticOverflow
                                or SqlErrorCodes.SqlServer.ArithmeticOverflowForDataType
                    )
            );
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

        return _ReadSqliteExtendedErrorCode(fault) is { } code && (code & 0xff) is 5 or 6;
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

        return _ReadSqliteExtendedErrorCode(exception) is _SqliteConstraintUnique or _SqliteConstraintPrimaryKey;
    }

    private static string? _ReadPostgreSqlConstraintName(DbException exception)
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

    private static int? _ReadSqliteExtendedErrorCode(DbException exception)
    {
        if (!string.Equals(exception.GetType().FullName, _SqliteExceptionTypeName, StringComparison.Ordinal))
        {
            return null;
        }

        return _SqliteExtendedErrorCodeProperties
                .GetOrAdd(
                    exception.GetType(),
                    static type =>
                        type.GetProperty("SqliteExtendedErrorCode", BindingFlags.Public | BindingFlags.Instance)
                )
                ?.GetValue(exception) as int?;
    }

    /// <summary>
    /// Finds error 547 among a SqlClient exception's errors and reads, from its English message, whether a CHECK
    /// constraint or a foreign key raised it and the constraint's name. A localized message yields a foreign key with
    /// no name, because the number alone cannot tell the two apart.
    /// </summary>
    private static (bool IsCheck, string? Name)? _FindSqlServerConstraintConflict(DbException exception)
    {
        if (!_IsSqlClientException(exception))
        {
            return null;
        }

        var found = false;

        if (
            _ErrorsProperties
                .GetOrAdd(exception.GetType(), static type => type.GetProperty("Errors"))
                ?.GetValue(exception)
            is IEnumerable errors
        )
        {
            foreach (var error in errors)
            {
                if (error is null || _ReadNumber(error) is not SqlErrorCodes.SqlServer.ConstraintViolation)
                {
                    continue;
                }

                found = true;

                if (_ReadMessage(error) is { } message && _ParseSqlServerConstraintConflict(message) is { } parsed)
                {
                    return parsed;
                }
            }
        }

        // An exception whose errors carry no readable message still reports the first error as its own Number and
        // Message, as _ReadErrorNumbers falls back to Number.
        if (_ReadNumber(exception) is SqlErrorCodes.SqlServer.ConstraintViolation)
        {
            return _ParseSqlServerConstraintConflict(exception.Message) ?? (false, null);
        }

        return found ? (false, null) : null;
    }

    private static (bool IsCheck, string? Name)? _ParseSqlServerConstraintConflict(string message)
    {
        var match = _SqlServerConstraintConflict.Match(message);

        if (!match.Success)
        {
            return null;
        }

        var isCheck = match.Groups["kind"].Value.Contains("CHECK", StringComparison.OrdinalIgnoreCase);

        return (isCheck, match.Groups["name"].Value);
    }

    private static string? _ReadUniqueConstraintName(DbException exception)
    {
        if (string.Equals(exception.SqlState, SqlErrorCodes.PostgreSql.UniqueViolation, StringComparison.Ordinal))
        {
            return _ReadPostgreSqlConstraintName(exception);
        }

        if (!_IsSqlClientException(exception))
        {
            return null;
        }

        // The duplicate key is not always the first error, so the message is read from the error that reported it.
        if (
            _ErrorsProperties
                .GetOrAdd(exception.GetType(), static type => type.GetProperty("Errors"))
                ?.GetValue(exception)
            is IEnumerable errors
        )
        {
            foreach (var error in errors)
            {
                if (
                    error is not null
                    && _ReadNumber(error) is { } number
                    && _ReadMessage(error) is { } message
                    && _ParseSqlServerConstraintName(number, message) is { } name
                )
                {
                    return name;
                }
            }
        }

        // An exception whose errors carry no readable message still reports the first error as its own Number and
        // Message, as _ReadErrorNumbers falls back to Number.
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

    // Error 2601: "Cannot insert duplicate key row in object 'dbo.T' with unique index 'IX_T_Email'. The duplicate key
    // value is (...)." Older servers end the message after the name. The lazy name stops only where that known
    // continuation, or the end of a line, follows "'.", so a name holding "'. " still parses.
    [GeneratedRegex(
        pattern: @"\Acannot insert duplicate key row in object '.+?' with unique index '(?<name>.+?)'\.(?: the duplicate key value is|\r?$)",
        options: RegexOptions.IgnoreCase
            | RegexOptions.CultureInvariant
            | RegexOptions.ExplicitCapture
            | RegexOptions.Multiline,
        matchTimeoutMilliseconds: RegexPatterns.MatchTimeoutMilliseconds
    )]
    private static partial Regex _SqlServerDuplicateIndex { get; }

    // Error 547: 'The DELETE statement conflicted with the REFERENCE constraint "FK_T_P". The conflict occurred in
    // database "db", table "dbo.T", column 'PId'.' The kind is FOREIGN KEY, REFERENCE, SAME TABLE REFERENCE, FOREIGN KEY
    // SAME TABLE, or CHECK. The lazy name stops at the first '". The conflict occurred', so a name holding a quote still
    // parses.
    [GeneratedRegex(
        pattern: @"\Athe [a-z ]+? statement conflicted with the (?<kind>[a-z ]+?) constraint ""(?<name>.+?)""\. the conflict occurred",
        options: RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: RegexPatterns.MatchTimeoutMilliseconds
    )]
    private static partial Regex _SqlServerConstraintConflict { get; }

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
