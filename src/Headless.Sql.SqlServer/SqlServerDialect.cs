// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text;
using Headless.Constants;
using Microsoft.Data.SqlClient;

namespace Headless.Sql.SqlServer;

/// <summary>The SQL Server dialect: PascalCase names, <c>_BIN2</c> key collation, and a clock variable.</summary>
/// <remarks>
/// <para>
/// <c>SYSUTCDATETIME()</c> is evaluated once when a statement starts, before it waits on any lock, so a statement
/// that decides on the clock never reads it inline: the clock is captured into <c>@now</c> in a statement of its own,
/// after the locking read has waited out any other holder.
/// </para>
/// <para>
/// Row locks are <c>UPDLOCK, HOLDLOCK, ROWLOCK</c>: <c>HOLDLOCK</c> keeps the lock to the end of the transaction and,
/// when the row is absent, locks the key range on the clustered primary key, so two first writers of one key
/// serialize and an insert never collides. Claims and batch deletes skip locked rows with <c>READPAST</c>, plus
/// <c>READCOMMITTEDLOCK</c> so they behave the same whether or not the database runs read committed snapshot isolation
/// (<c>READPAST</c> is refused under a snapshot read, and the hint is the default otherwise).
/// </para>
/// </remarks>
[PublicAPI]
public sealed class SqlServerDialect : ISqlDialect
{
    private const string _Clock = "DECLARE @now datetimeoffset(7) = TODATETIMEOFFSET(SYSUTCDATETIME(), 0);";
    private const string _SkipLocked = "UPDLOCK, READPAST, ROWLOCK, READCOMMITTEDLOCK";

    private SqlServerDialect() { }

    /// <summary>Gets the dialect. It is stateless.</summary>
    public static SqlServerDialect Instance { get; } = new();

    public string DisplayName => "SQL Server";

    public TimeSpan TimestampPrecision => TimeSpan.FromTicks(1);

    public Type ConnectionType => typeof(SqlConnection);

    public Type TransactionType => typeof(SqlTransaction);

    public DbConnection CreateConnection(string connectionString)
    {
        return new SqlConnection(connectionString);
    }

    public string Name(string pascalName)
    {
        return pascalName;
    }

    public string Quote(string identifier)
    {
        return $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";
    }

    public string Qualify(string schema, string identifier)
    {
        return $"{Quote(schema)}.{Quote(identifier)}";
    }

    public string NextSequenceValue(string qualifiedSequence)
    {
        return $"NEXT VALUE FOR {qualifiedSequence}";
    }

    public string ShiftByDuration(string instant, string parameter, bool subtract = false)
    {
        // One DATEADD per unit: a single DATEADD takes an int, which overflows in nanoseconds after about two seconds
        // and in seconds after about 68 years.
        var sign = subtract ? "-" : "";

        return $"DATEADD(nanosecond, {sign}@{parameter}Nanoseconds, DATEADD(second, {sign}@{parameter}Seconds, DATEADD(day, {sign}@{parameter}Days, {instant})))";
    }

    public void AddDuration(DbCommand command, string parameter, TimeSpan duration)
    {
        var days = checked((int)(duration.Ticks / TimeSpan.TicksPerDay));
        var ticksWithinDay = duration.Ticks % TimeSpan.TicksPerDay;

        command.Parameters.Add(new SqlParameter(parameter + "Days", SqlDbType.Int) { Value = days });
        command.Parameters.Add(
            new SqlParameter(parameter + "Seconds", SqlDbType.Int)
            {
                Value = (int)(ticksWithinDay / TimeSpan.TicksPerSecond),
            }
        );
        command.Parameters.Add(
            new SqlParameter(parameter + "Nanoseconds", SqlDbType.Int)
            {
                Value = (int)(ticksWithinDay % TimeSpan.TicksPerSecond * 100),
            }
        );
    }

    public void AddParameter(DbCommand command, string parameter, SqlColumnType type, object? value)
    {
        var sqlParameter = type.Kind switch
        {
            // Sized to the column, so the plan is reused across values and the comparison keeps the column's collation.
            SqlColumnKind.KeyText or SqlColumnKind.Text => new SqlParameter(
                parameter,
                SqlDbType.NVarChar,
                type.MaxLength
            ),
            SqlColumnKind.Int16 => new SqlParameter(parameter, SqlDbType.SmallInt),
            SqlColumnKind.Int32 => new SqlParameter(parameter, SqlDbType.Int),
            SqlColumnKind.Int64 => new SqlParameter(parameter, SqlDbType.BigInt),
            SqlColumnKind.Timestamp => new SqlParameter(parameter, SqlDbType.DateTimeOffset),
            SqlColumnKind.Binary => new SqlParameter(parameter, SqlDbType.VarBinary, -1),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type.Kind, "Unknown column kind."),
        };

        sqlParameter.Value = value switch
        {
            null => DBNull.Value,
            ReadOnlyMemory<byte> bytes => bytes.ToArray(),
            _ => value,
        };

        command.Parameters.Add(sqlParameter);
    }

    public string KeysetAfter(IReadOnlyList<string> columns, IReadOnlyList<string> parameters)
    {
        // T-SQL has no row-value comparison: (a, b, c) > (x, y, z) expanded.
        var terms = new List<string>(columns.Count);

        for (var i = 0; i < columns.Count; i++)
        {
            var equalities = Enumerable.Range(0, i).Select(j => $"{columns[j]} = @{parameters[j]}");

            terms.Add("(" + string.Join(" AND ", equalities.Append($"{columns[i]} > @{parameters[i]}")) + ")");
        }

        return "(" + string.Join(" OR ", terms) + ")";
    }

    public string Render(SqlLockedRead statement)
    {
        return $"""
            SELECT {string.Join(", ", statement.Columns)}
            FROM {statement.Table} WITH (UPDLOCK, HOLDLOCK, ROWLOCK)
            WHERE {_Key(statement.Key, alias: null)};
            """;
    }

    public string Render(SqlFencedTransition statement)
    {
        var fence = _Clocked(statement.Fence);

        if (statement.Set is null)
        {
            return $"""
                {_Clock}
                SELECT CAST(CASE WHEN EXISTS (
                    SELECT 1 FROM {statement.Table} WHERE {_Key(statement.Key, alias: null)} AND ({fence})
                ) THEN 1 ELSE 0 END AS bit);
                """;
        }

        // The UPDATE is its own guard: its WHERE carries the fence, so the check and the write are one decision. The
        // row stays locked by the preceding locking read, so the final read returns exactly what this batch wrote.
        return $"""
            {_Clock}
            UPDATE {statement.Table}
            SET {_Clocked(statement.Set)}
            WHERE {_Key(statement.Key, alias: null)} AND ({fence});
            {_AppliedAndReturning(statement.Table, statement.Key, statement.Returning)}
            """;
    }

    public string Render(SqlInsertIfAbsent statement)
    {
        var columns = statement.Key.Select(static k => k.Column).Concat(statement.Columns);
        var values = statement.Key.Select(static k => "@" + k.Parameter).Concat(statement.Values.Select(_Clocked));

        // The existence check takes the key-range lock, so a concurrent insert of the same key waits here instead of
        // raising a duplicate-key error, which would doom a caller's transaction running with XACT_ABORT ON.
        return $"""
            {_Clock}
            INSERT INTO {statement.Table} ({string.Join(", ", columns)})
            SELECT {string.Join(", ", values)}
            WHERE NOT EXISTS (
                SELECT 1 FROM {statement.Table} WITH (UPDLOCK, HOLDLOCK, ROWLOCK) WHERE {_Key(
                statement.Key,
                alias: null
            )}
            );
            {_AppliedAndReturning(statement.Table, statement.Key, statement.Returning)}
            """;
    }

    public string Render(SqlClaimNext statement)
    {
        // Nothing waits, so the clock can be read first.
        return $"""
            {_Clock}
            WITH candidate AS (
                SELECT TOP (1) *
                FROM {statement.Table} WITH ({_SkipLocked})
                WHERE {_Clocked(statement.Filter)}
                ORDER BY {string.Join(", ", statement.OrderBy)}
            )
            UPDATE candidate
            SET {_Clocked(statement.Set)}
            OUTPUT {string.Join(", ", statement.Returning.Select(static c => "inserted." + c))};
            """;
    }

    public string Render(SqlDeleteBatch statement)
    {
        return $"""
            {_Clock}
            WITH doomed AS (
                SELECT TOP (@{statement.BatchSizeParameter}) *
                FROM {statement.Table} WITH ({_SkipLocked})
                WHERE {_Clocked(statement.Filter)}
            )
            DELETE FROM doomed;
            """;
    }

    public SqlErrorKind Classify(Exception exception)
    {
        return exception is SqlException { Number: var number }
            ? number switch
            {
                SqlErrorCodes.SqlServer.DuplicateKeyUniqueIndex
                or SqlErrorCodes.SqlServer.DuplicateKeyUniqueConstraint => SqlErrorKind.UniqueViolation,
                SqlErrorCodes.SqlServer.DeadlockVictim => SqlErrorKind.Deadlock,
                SqlErrorCodes.SqlServer.SnapshotUpdateConflict => SqlErrorKind.SerializationConflict,
                2714 or 1913 or 2759 => SqlErrorKind.DuplicateObject,
                1222 => SqlErrorKind.LockTimeout,
                _ => SqlErrorKind.None,
            }
            : SqlErrorKind.None;
    }

    private static string _AppliedAndReturning(
        string table,
        IReadOnlyList<SqlKeyColumn> key,
        IReadOnlyList<string> returning
    )
    {
        // @@ROWCOUNT must be read by the very next statement. The re-read runs under the lock the batch already holds.
        var columns = returning.Count == 0 ? "" : ", " + string.Join(", ", returning.Select(static c => "t." + c));

        return $"""
            DECLARE @applied bit = CASE WHEN @@ROWCOUNT > 0 THEN 1 ELSE 0 END;
            SELECT @applied{columns}
            FROM (SELECT 1 AS one) AS x
            LEFT JOIN {table} AS t ON @applied = 1 AND {_Key(key, "t")};
            """;
    }

    private static string _Clocked(string fragment)
    {
        return fragment.Replace(SqlDialectTokens.Now, "@now", StringComparison.Ordinal);
    }

    private static string _Key(IReadOnlyList<SqlKeyColumn> key, string? alias)
    {
        var prefix = alias is null ? "" : alias + ".";

        return string.Join(" AND ", key.Select(k => $"{prefix}{k.Column} = @{k.Parameter}"));
    }
}
