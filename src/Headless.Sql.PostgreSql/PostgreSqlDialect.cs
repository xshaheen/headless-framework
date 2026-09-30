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

    public string Render(SqlLockedRead statement)
    {
        return $"""
            SELECT {string.Join(", ", statement.Columns)}
            FROM {statement.Table}
            WHERE {_Key(statement.Key, alias: null)}
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
                ON CONFLICT ({string.Join(", ", statement.Key.Select(static k => k.Column))}) DO NOTHING
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
                LIMIT 1
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

    public string Render(SqlSchemaScript script)
    {
        // The feature's own lock serializes replicas creating the same objects; the schema-wide lock, taken second so
        // the order is the same in every feature, stops another feature's concurrent CREATE SCHEMA failing this one.
        var builder = new StringBuilder();
        builder.AppendLine(
            CultureInfo.InvariantCulture,
            $"SELECT pg_advisory_xact_lock(hashtextextended(@{script.LockResourceParameter}, 0));"
        );
        builder.AppendLine(PostgreSqlSchemaInitLock.AcquireStatement(script.Schema));
        builder.AppendLine(CultureInfo.InvariantCulture, $"CREATE SCHEMA IF NOT EXISTS {Quote(script.Schema)};");

        foreach (var sequence in script.Sequences)
        {
            builder.AppendLine(
                CultureInfo.InvariantCulture,
                $"CREATE SEQUENCE IF NOT EXISTS {Qualify(script.Schema, sequence)} AS bigint START WITH 1 INCREMENT BY 1 NO CYCLE;"
            );
        }

        foreach (var table in script.Tables)
        {
            var qualified = Qualify(script.Schema, table.Name);
            var parts = table
                .Columns.Select(c =>
                    $"{c.Name} {_TypeName(c.Type)}{(c.Nullable ? " NULL" : " NOT NULL")}{(c.Default is null ? "" : " DEFAULT " + c.Default)}"
                )
                .Append($"CONSTRAINT {Quote(table.PrimaryKeyName)} PRIMARY KEY ({string.Join(", ", table.PrimaryKey)})")
                .Concat(table.Checks.Select(c => $"CONSTRAINT {Quote(c.Name)} CHECK ({c.Condition})"));

            builder.AppendLine(CultureInfo.InvariantCulture, $"CREATE TABLE IF NOT EXISTS {qualified} (");
            builder.Append("    ").AppendJoin("," + Environment.NewLine + "    ", parts).AppendLine();
            builder.AppendLine(");");

            foreach (var index in table.Indexes)
            {
                builder.AppendLine(
                    CultureInfo.InvariantCulture,
                    $"CREATE INDEX IF NOT EXISTS {Quote(index.Name)} ON {qualified} ({string.Join(", ", index.Columns)}){(index.Filter is null ? "" : " WHERE " + index.Filter)};"
                );
            }
        }

        return builder.ToString();
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
                _ => SqlErrorKind.None,
            }
            : SqlErrorKind.None;
    }

    private static string _Clocked(string fragment)
    {
        return fragment.Replace(SqlDialectTokens.Now, "clock.now", StringComparison.Ordinal);
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

    private static string _TypeName(SqlColumnType type)
    {
        return type.Kind switch
        {
            SqlColumnKind.KeyText => string.Create(
                CultureInfo.InvariantCulture,
                $"varchar({type.MaxLength}) COLLATE \"C\""
            ),
            SqlColumnKind.Text => string.Create(CultureInfo.InvariantCulture, $"varchar({type.MaxLength})"),
            SqlColumnKind.Int16 => "smallint",
            SqlColumnKind.Int32 => "integer",
            SqlColumnKind.Int64 => "bigint",
            SqlColumnKind.Timestamp => "timestamptz",
            SqlColumnKind.Binary => "bytea",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type.Kind, "Unknown column kind."),
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
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown column kind."),
        };
    }
}
