// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Headless.Constants;
using Npgsql;
using NpgsqlTypes;

namespace Headless.Sql.PostgreSql;

/// <summary>The PostgreSQL dialect: snake_case names, <c>"C"</c> key collation, and <c>clock_timestamp()</c>.</summary>
/// <remarks>
/// The clock is <c>clock_timestamp()</c> captured once in a <c>MATERIALIZED</c> CTE, never <c>now()</c>: <c>now()</c>
/// is frozen at transaction start, so inside a long transaction it would keep an expired row looking live. Row locks
/// are <c>FOR NO KEY UPDATE</c> (update intent, compatible with foreign-key share locks), and claims skip locked rows.
/// </remarks>
[PublicAPI]
public sealed class PostgreSqlDialect : ISqlDialect
{
    private const string _Clock = "WITH clock AS MATERIALIZED (SELECT clock_timestamp() AS now)";

    private PostgreSqlDialect() { }

    /// <summary>Gets the dialect. It is stateless.</summary>
    public static PostgreSqlDialect Instance { get; } = new();

    public string DisplayName => "PostgreSQL";

    public TimeSpan TimestampPrecision => TimeSpan.FromMicroseconds(1);

    public Type ConnectionType => typeof(NpgsqlConnection);

    public Type TransactionType => typeof(NpgsqlTransaction);

    public DbConnection CreateConnection(string connectionString)
    {
        return new NpgsqlConnection(connectionString);
    }

    public string Name(string pascalName)
    {
        // "PK_FencingLeases" -> "pk_fencing_leases": each underscore-separated part converted on its own.
        return string.Join(
            '_',
            pascalName.Split('_').Select(static part => JsonNamingPolicy.SnakeCaseLower.ConvertName(part))
        );
    }

    public string Quote(string identifier)
    {
        return $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    public string Qualify(string schema, string identifier)
    {
        return $"{Quote(schema)}.{Quote(identifier)}";
    }

    public string NextSequenceValue(string qualifiedSequence)
    {
        return $"nextval('{qualifiedSequence.Replace("'", "''", StringComparison.Ordinal)}')";
    }

    public string ShiftByDuration(string instant, string parameter, bool subtract = false)
    {
        return $"{instant} {(subtract ? '-' : '+')} @{parameter}";
    }

    public string BooleanLiteral(bool value)
    {
        return value ? "TRUE" : "FALSE";
    }

    public string NewGuid()
    {
        return "gen_random_uuid()";
    }

    public string ShiftBySeconds(string instant, string seconds)
    {
        return $"({instant} + ({seconds}) * INTERVAL '1 second')";
    }

    public string Limit(string limitParameter, string? offsetParameter = null)
    {
        return offsetParameter is null
            ? $"LIMIT @{limitParameter}"
            : $"LIMIT @{limitParameter} OFFSET @{offsetParameter}";
    }

    public string LikeIgnoringCase(string expression, string patternParameter)
    {
        return $"{expression} ILIKE @{patternParameter} ESCAPE '\\'";
    }

    public string ReadWithoutWaiting(string table)
    {
        // A plain read takes no row lock and sees the last committed version, so it never waits.
        return table;
    }

    public void AddDuration(DbCommand command, string parameter, TimeSpan duration)
    {
        command.Parameters.Add(
            new NpgsqlParameter<TimeSpan>(parameter, NpgsqlDbType.Interval) { TypedValue = duration }
        );
    }

    public void AddParameter(DbCommand command, string parameter, SqlColumnType type, object? value)
    {
        command.Parameters.Add(
            new NpgsqlParameter(parameter, _DbType(type.Kind))
            {
                Value = value switch
                {
                    null => DBNull.Value,
                    // timestamptz accepts only UTC offsets.
                    DateTimeOffset instant => instant.ToUniversalTime(),
                    ReadOnlyMemory<byte> bytes => bytes.ToArray(),
                    _ => value,
                },
            }
        );
    }

    public string KeysetAfter(IReadOnlyList<string> columns, IReadOnlyList<string> parameters)
    {
        // A row-value comparison, which the planner turns into one range scan of an index on the same columns.
        return $"({string.Join(", ", columns)}) > ({string.Join(", ", parameters.Select(static p => "@" + p))})";
    }

    public string InList(string expression, string parameter, SqlColumnType elementType)
    {
        _EnsureListable(elementType);

        return $"{expression} = ANY(@{parameter})";
    }

    public void AddListParameter<T>(
        DbCommand command,
        string parameter,
        SqlColumnType elementType,
        IReadOnlyCollection<T> values
    )
    {
        command.Parameters.Add(CreateListParameter(parameter, elementType, values));
    }

    public DbParameter CreateListParameter<T>(
        string parameter,
        SqlColumnType elementType,
        IReadOnlyCollection<T> values
    )
    {
        _EnsureListable(elementType);
        Array array = values is IReadOnlyCollection<DateTimeOffset> instants
            ? instants.Select(static i => i.ToUniversalTime()).ToArray()
            : values.ToArray();

        // Npgsql types the parameter from the element type of the CLR array (string[] -> text[], Guid[] -> uuid[]).
        return new NpgsqlParameter(parameter, array);
    }

    public string InTuples(
        IReadOnlyList<string> expressions,
        string parameter,
        IReadOnlyList<SqlColumnType> elementTypes
    )
    {
        _ValidateTuples(expressions, elementTypes, _EnsureListable);
        var arrays = string.Join(
            ", ",
            Enumerable.Range(0, elementTypes.Count).Select(i => "@" + _TupleParameter(parameter, i))
        );

        // unnest over several arrays zips them into rows, so each row stays one tuple.
        return $"({string.Join(", ", expressions)}) IN (SELECT * FROM unnest({arrays}))";
    }

    public IReadOnlyList<DbParameter> CreateTupleListParameters(
        string parameter,
        IReadOnlyList<SqlColumnType> elementTypes,
        IReadOnlyCollection<IReadOnlyList<object>> rows
    )
    {
        _ValidateTupleRows(elementTypes, rows, _EnsureListable);
        var parameters = new DbParameter[elementTypes.Count];

        for (var column = 0; column < elementTypes.Count; column++)
        {
            var array = Array.CreateInstance(_ClrType(elementTypes[column].Kind), rows.Count);
            var index = 0;

            foreach (var row in rows)
            {
                array.SetValue(
                    row[column] is DateTimeOffset instant ? instant.ToUniversalTime() : row[column],
                    index++
                );
            }

            // Npgsql types each array from its CLR element type.
            parameters[column] = new NpgsqlParameter(_TupleParameter(parameter, column), array);
        }

        return parameters;
    }

    public string Render(SqlLockedRead statement)
    {
        return $"""
            SELECT {string.Join(", ", statement.Columns)}
            FROM {statement.Table}
            WHERE {_Key(statement.Key, alias: null)}{_And(statement.KeyPredicate)}
            FOR NO KEY UPDATE;
            """;
    }

    public string Render(SqlFencedTransition statement)
    {
        var fence = _Clocked(statement.Fence);

        if (statement.Set is null)
        {
            return $"""
                {_Clock}
                SELECT EXISTS (SELECT 1 FROM {statement.Table} WHERE {_Key(statement.Key, alias: null)} AND ({fence}))
                FROM clock;
                """;
        }

        // SET expressions read the row as it was, and the UPDATE's WHERE is the fence: the check and the write are
        // one decision on one clock value.
        return $"""
            {_Clock},
            changed AS (
                UPDATE {statement.Table}
                SET {_Clocked(statement.Set)}
                FROM clock
                WHERE {_Key(statement.Key, alias: null)} AND ({fence})
                RETURNING {string.Join(", ", statement.Returning)}
            )
            SELECT EXISTS (SELECT 1 FROM changed){_Prefixed(statement.Returning, "changed")}
            FROM clock
            LEFT JOIN changed ON TRUE;
            """;
    }

    public string Render(SqlInsertIfAbsent statement)
    {
        var columns = statement.Key.Select(static k => k.Column).Concat(statement.Columns);
        var values = statement.Key.Select(static k => "@" + k.Parameter).Concat(statement.Values.Select(_Clocked));

        // No key-range lock exists for an absent row, so a concurrent insert of the same key waits for the other
        // transaction and then inserts nothing, instead of raising a duplicate-key error.
        return $"""
            {_Clock},
            inserted AS (
                INSERT INTO {statement.Table} ({string.Join(", ", columns)})
                SELECT {string.Join(", ", values)}
                FROM clock
                ON CONFLICT ({string.Join(", ", statement.Key.Select(static k => k.Column))}){_Where(
                statement.KeyPredicate
            )} DO NOTHING
                RETURNING {string.Join(", ", statement.Returning)}
            )
            SELECT EXISTS (SELECT 1 FROM inserted){_Prefixed(statement.Returning, "inserted")}
            FROM clock
            LEFT JOIN inserted ON TRUE;
            """;
    }

    public string Render(SqlClaimNext statement)
    {
        return $"""
            {_Clock},
            candidate AS (
                SELECT {_Prefixed(statement.KeyColumns, "c", leadingComma: false)}
                FROM {statement.Table} AS c, clock
                WHERE {_Clocked(statement.Filter)}
                ORDER BY {string.Join(", ", statement.OrderBy)}
                LIMIT {(statement.BatchSizeParameter is null ? "1" : "@" + statement.BatchSizeParameter)}
                FOR UPDATE OF c SKIP LOCKED
            )
            UPDATE {statement.Table} AS target
            SET {_Clocked(statement.Set)}
            FROM candidate, clock
            WHERE {_JoinOn(statement.KeyColumns, "target", "candidate")}
            RETURNING {_Prefixed(statement.Returning, "target", leadingComma: false)};
            """;
    }

    public string Render(SqlDeleteBatch statement)
    {
        return $"""
            {_Clock},
            doomed AS (
                SELECT {_Prefixed(statement.KeyColumns, "d", leadingComma: false)}
                FROM {statement.Table} AS d, clock
                WHERE {_Clocked(statement.Filter)}
                LIMIT @{statement.BatchSizeParameter}
                FOR UPDATE OF d SKIP LOCKED
            )
            DELETE FROM {statement.Table} AS target
            USING doomed
            WHERE {_JoinOn(statement.KeyColumns, "target", "doomed")};
            """;
    }

    public string Render(SqlUpsert statement)
    {
        var keyColumns = string.Join(", ", statement.Key.Select(static k => k.Column));
        var columns = statement.Key.Select(static k => k.Column).Concat(statement.Columns);
        var values = statement.Key.Select(static k => "@" + k.Parameter).Concat(statement.Values.Select(_Clocked));
        var guard = statement.Guard is null ? "" : $"\n        WHERE {_Stored(_ClockedSubquery(statement.Guard))}";

        // ON CONFLICT DO UPDATE has no FROM clause, so the update reads the clock through a subquery. xmax is zero only
        // on a row version this statement created, which tells an insert from an update. A refused guard returns no
        // row, so the outcome defaults to Refused.
        return $"""
            {_Clock},
            upserted AS (
                INSERT INTO {statement.Table} AS stored ({string.Join(", ", columns)})
                SELECT {string.Join(", ", values)}
                FROM clock
                ON CONFLICT ({keyColumns}) DO UPDATE
                SET {_Stored(_ClockedSubquery(statement.Set))}{guard}
                RETURNING (stored.xmax = 0) AS was_inserted{_Prefixed(statement.Returning, "stored")}
            )
            SELECT CAST(CASE WHEN upserted.was_inserted IS NULL THEN {(int)
                SqlUpsertOutcome.Refused} WHEN upserted.was_inserted THEN {(int)SqlUpsertOutcome.Inserted} ELSE {(int)
                SqlUpsertOutcome.Updated} END AS smallint){_Prefixed(statement.Returning, "upserted")}
            FROM clock
            LEFT JOIN upserted ON TRUE;
            """;
    }

    public string Render(SqlClockedStatement statement)
    {
        return $"""
            {_Clock}
            {_ClockedSubquery(statement.Sql)}
            """;
    }

    public string Render(SqlInsert statement)
    {
        return $"""
            {_Clock}
            INSERT INTO {statement.Table} ({string.Join(", ", statement.Columns)})
            SELECT {string.Join(", ", statement.Values.Select(_Clocked))}
            FROM clock
            RETURNING {string.Join(", ", statement.Returning)};
            """;
    }

    public string Render(SqlLockBatch statement)
    {
        // FOR UPDATE rather than FOR NO KEY UPDATE: the locked rows may be deleted later in the same transaction.
        return $"""
            {_Clock}
            SELECT {_Clocked(string.Join(", ", statement.Columns))}
            FROM {statement.Table} AS l, clock
            WHERE {_Clocked(statement.Filter)}
            ORDER BY {string.Join(", ", statement.OrderBy)}
            LIMIT @{statement.BatchSizeParameter}
            FOR UPDATE OF l SKIP LOCKED;
            """;
    }

    public string Render(SqlTransactionLock statement)
    {
        // hashtextextended maps the name onto the 64-bit advisory-lock key space; a transaction-level lock ends with
        // the transaction, so no release can be forgotten.
        return $"SELECT pg_advisory_xact_lock(hashtextextended(@{statement.ResourceParameter}, 0));";
    }

    public SqlErrorKind Classify(Exception exception)
    {
        return exception is PostgresException { SqlState: var state }
            ? state switch
            {
                SqlErrorCodes.PostgreSql.UniqueViolation => SqlErrorKind.UniqueViolation,
                SqlErrorCodes.PostgreSql.DeadlockDetected => SqlErrorKind.Deadlock,
                SqlErrorCodes.PostgreSql.SerializationFailure => SqlErrorKind.SerializationConflict,
                SqlErrorCodes.PostgreSql.DuplicateSchema
                or SqlErrorCodes.PostgreSql.DuplicateTable
                or SqlErrorCodes.PostgreSql.DuplicateObject => SqlErrorKind.DuplicateObject,
                SqlErrorCodes.PostgreSql.LockTimeout => SqlErrorKind.LockTimeout,
                SqlErrorCodes.PostgreSql.InFailedSqlTransaction => SqlErrorKind.TransactionAborted,
                _ => SqlErrorKind.None,
            }
            : SqlErrorKind.None;
    }

    private static string _Clocked(string fragment)
    {
        return fragment.Replace(SqlDialectTokens.Now, "clock.now", StringComparison.Ordinal);
    }

    private static string _And(string? predicate)
    {
        return predicate is null ? "" : $" AND ({predicate})";
    }

    private static string _Where(string? predicate)
    {
        return predicate is null ? "" : $" WHERE {predicate}";
    }

    private static string _Stored(string fragment)
    {
        return fragment.Replace(SqlDialectTokens.Stored, "stored", StringComparison.Ordinal);
    }

    private static string _ClockedSubquery(string fragment)
    {
        return fragment.Replace(SqlDialectTokens.Now, "(SELECT clock.now FROM clock)", StringComparison.Ordinal);
    }

    private static void _EnsureListable(SqlColumnType elementType)
    {
        if (elementType.Kind is SqlColumnKind.Binary or SqlColumnKind.Json)
        {
            throw new ArgumentException("A list parameter cannot hold binary or JSON values.", nameof(elementType));
        }
    }

    private static string _Key(IReadOnlyList<SqlKeyColumn> key, string? alias)
    {
        var prefix = alias is null ? "" : alias + ".";

        return string.Join(" AND ", key.Select(k => $"{prefix}{k.Column} = @{k.Parameter}"));
    }

    private static string _JoinOn(IReadOnlyList<string> columns, string left, string right)
    {
        return string.Join(" AND ", columns.Select(c => $"{left}.{c} = {right}.{c}"));
    }

    private static string _Prefixed(IReadOnlyList<string> columns, string alias, bool leadingComma = true)
    {
        var list = string.Join(", ", columns.Select(c => $"{alias}.{c}"));

        return leadingComma ? (list.Length == 0 ? "" : ", " + list) : list;
    }

    private static void _ValidateTuples(
        IReadOnlyList<string> expressions,
        IReadOnlyList<SqlColumnType> elementTypes,
        Action<SqlColumnType> ensureListable
    )
    {
        if (expressions.Count < 2 || expressions.Count != elementTypes.Count)
        {
            throw new ArgumentException(
                "A tuple list needs at least two expressions and one element type per expression.",
                nameof(elementTypes)
            );
        }

        foreach (var type in elementTypes)
        {
            ensureListable(type);
        }
    }

    private static void _ValidateTupleRows(
        IReadOnlyList<SqlColumnType> elementTypes,
        IReadOnlyCollection<IReadOnlyList<object>> rows,
        Action<SqlColumnType> ensureListable
    )
    {
        foreach (var type in elementTypes)
        {
            ensureListable(type);
        }

        foreach (var row in rows)
        {
            if (row.Count != elementTypes.Count || row.Any(static value => value is null))
            {
                throw new ArgumentException("Every tuple row needs one non-null value per element type.", nameof(rows));
            }
        }
    }

    private static string _TupleParameter(string parameter, int column)
    {
        return parameter + "_" + column.ToString(CultureInfo.InvariantCulture);
    }

    private static Type _ClrType(SqlColumnKind kind)
    {
        return kind switch
        {
            SqlColumnKind.KeyText or SqlColumnKind.Text => typeof(string),
            SqlColumnKind.Int16 => typeof(short),
            SqlColumnKind.Int32 => typeof(int),
            SqlColumnKind.Int64 => typeof(long),
            SqlColumnKind.Timestamp => typeof(DateTimeOffset),
            SqlColumnKind.Guid => typeof(Guid),
            SqlColumnKind.Boolean => typeof(bool),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "This column kind cannot be listed."),
        };
    }

    private static NpgsqlDbType _DbType(SqlColumnKind kind)
    {
        return kind switch
        {
            SqlColumnKind.KeyText or SqlColumnKind.Text => NpgsqlDbType.Varchar,
            SqlColumnKind.Int16 => NpgsqlDbType.Smallint,
            SqlColumnKind.Int32 => NpgsqlDbType.Integer,
            SqlColumnKind.Int64 => NpgsqlDbType.Bigint,
            SqlColumnKind.Timestamp => NpgsqlDbType.TimestampTz,
            SqlColumnKind.Binary => NpgsqlDbType.Bytea,
            SqlColumnKind.Guid => NpgsqlDbType.Uuid,
            SqlColumnKind.Boolean => NpgsqlDbType.Boolean,
            SqlColumnKind.Json => NpgsqlDbType.Jsonb,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown column kind."),
        };
    }
}
