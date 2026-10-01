// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.Data.Common;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Headless.Sql.Sqlite;

/// <summary>
/// The SQLite dialect: snake_case names, one database write lock in place of row locks, and instants stored as
/// fixed-width UTC text.
/// </summary>
/// <remarks>
/// <para>
/// SQLite admits one writer per database file and has no row locks, so the dialect's locking shapes rest on that write
/// lock. <c>Microsoft.Data.Sqlite</c> begins every transaction with <c>BEGIN IMMEDIATE</c> unless asked for a deferred
/// one, so a store's transaction holds the write lock from its first statement: every statement in it already runs
/// after any other writer has finished, which is the guarantee a row lock gives on the other engines. The locking read
/// also opens with a no-op write, so a deferred caller transaction takes the write lock there instead of reading first
/// and failing later with <c>SQLITE_BUSY</c> when it upgrades. A claim or batch delete never meets a row another
/// transaction holds, so there is nothing to skip.
/// </para>
/// <para>
/// Instants are stored as text in one fixed shape, <c>yyyy-MM-dd HH:mm:ss.ffffff+00:00</c>, always UTC and always six
/// fractional digits, so text comparison and ordering are instant comparison and ordering, and
/// <c>Microsoft.Data.Sqlite</c> reads the value back as a <see cref="DateTimeOffset" />. A bound instant is converted
/// to UTC and truncated to the microsecond. Duration arithmetic runs on integer microseconds, because SQLite's date
/// functions keep only milliseconds.
/// </para>
/// <para>
/// The database clock is SQLite's <c>'now'</c>, which the engine computes inside the statement from the clock of the
/// host that runs it, at millisecond resolution, and holds fixed for the whole statement. Every process that opens a
/// SQLite file must run on the host that holds it (file locking does not work across a network file system), so they
/// all read the same clock.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class SqliteDialect : ISqlDialect
{
    /// <summary>The engine clock, padded from SQLite's milliseconds to the stored microsecond shape.</summary>
    private const string _Clock = "(strftime('%Y-%m-%d %H:%M:%f', 'now') || '000+00:00')";

    private const string _InstantFormat = "yyyy-MM-dd HH:mm:ss.ffffff";
    private const string _UtcSuffix = "+00:00";

    private SqliteDialect() { }

    /// <summary>Gets the dialect. It is stateless.</summary>
    public static SqliteDialect Instance { get; } = new();

    public string DisplayName => "SQLite";

    public TimeSpan TimestampPrecision => TimeSpan.FromMicroseconds(1);

    public Type ConnectionType => typeof(SqliteConnection);

    public Type TransactionType => typeof(SqliteTransaction);

    public DbConnection CreateConnection(string connectionString)
    {
        return new SqliteConnection(connectionString);
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

    /// <inheritdoc />
    /// <remarks>
    /// SQLite has no schemas (its <c>schema.table</c> names an attached database file), so the schema becomes a
    /// prefix of the name: <c>headless</c> and <c>fencing_leases</c> make <c>"headless_fencing_leases"</c>. Objects
    /// of different schemas still never collide.
    /// </remarks>
    public string Qualify(string schema, string identifier)
    {
        return Quote(QualifiedName(schema, identifier));
    }

    /// <summary>Returns the unquoted name <see cref="Qualify" /> quotes, for DDL that names an index or a constraint.</summary>
    /// <param name="schema">The schema, which becomes the name's prefix.</param>
    /// <param name="identifier">The object's name within the schema.</param>
    public static string QualifiedName(string schema, string identifier)
    {
        return $"{schema}_{identifier}";
    }

    /// <inheritdoc />
    /// <remarks>
    /// SQLite has no sequence objects. <paramref name="qualifiedSequence" /> names a one-row table (<c>id</c> 1 and
    /// <c>value</c>, the last value issued), and the expression reads the value after it. Reading does not advance it:
    /// the DDL of the table that stores the drawn value must add triggers that raise the sequence row to every value
    /// written, which keeps the draw and the advance in one statement under the database write lock. One statement
    /// therefore draws one value, however many rows it writes.
    /// </remarks>
    public string NextSequenceValue(string qualifiedSequence)
    {
        return $"(SELECT value + 1 FROM {qualifiedSequence} WHERE id = 1)";
    }

    public string ShiftByDuration(string instant, string parameter, bool subtract = false)
    {
        // Through integer microseconds and back to the stored text shape. Integer division truncates toward zero, so a
        // negative total (an instant before 1970) borrows one second for its non-negative fraction.
        var micros =
            $"(unixepoch(substr({instant}, 1, 19)) * 1000000 + CAST(substr({instant}, 21, 6) AS INTEGER) {(subtract ? '-' : '+')} @{parameter})";

        return $"(strftime('%Y-%m-%d %H:%M:%S', {micros} / 1000000 - ({micros} % 1000000 < 0), 'unixepoch') || '.' || printf('%06d', ({micros} % 1000000 + 1000000) % 1000000) || '{_UtcSuffix}')";
    }

    public void AddDuration(DbCommand command, string parameter, TimeSpan duration)
    {
        command.Parameters.Add(
            new SqliteParameter(parameter, SqliteType.Integer) { Value = duration.Ticks / TimeSpan.TicksPerMicrosecond }
        );
    }

    public void AddParameter(DbCommand command, string parameter, SqlColumnType type, object? value)
    {
        command.Parameters.Add(
            new SqliteParameter(parameter, _StorageType(type.Kind)) { Value = _ToStorage(value) ?? DBNull.Value }
        );
    }

    public string KeysetAfter(IReadOnlyList<string> columns, IReadOnlyList<string> parameters)
    {
        return $"({string.Join(", ", columns)}) > ({string.Join(", ", parameters.Select(static p => "@" + p))})";
    }

    public string InList(string expression, string parameter, SqlColumnType elementType)
    {
        _EnsureListable(elementType);

        // The list travels as one JSON array; json_each yields its elements already in their stored form.
        return $"{expression} IN (SELECT value FROM json_each(@{parameter}))";
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

        return new SqliteParameter(parameter, SqliteType.Text)
        {
            Value = _Json(writer =>
            {
                foreach (var value in values)
                {
                    _WriteElement(writer, value);
                }
            }),
        };
    }

    public string InTuples(
        IReadOnlyList<string> expressions,
        string parameter,
        IReadOnlyList<SqlColumnType> elementTypes
    )
    {
        _ValidateTuples(expressions, elementTypes);
        var columns = string.Join(
            ", ",
            Enumerable
                .Range(0, elementTypes.Count)
                .Select(i => string.Create(CultureInfo.InvariantCulture, $"value ->> {i}"))
        );

        // Each row is one JSON array read back by position, so a row's values are matched together.
        return $"({string.Join(", ", expressions)}) IN (SELECT {columns} FROM json_each(@{parameter}))";
    }

    public IReadOnlyList<DbParameter> CreateTupleListParameters(
        string parameter,
        IReadOnlyList<SqlColumnType> elementTypes,
        IReadOnlyCollection<IReadOnlyList<object>> rows
    )
    {
        _ValidateTupleRows(elementTypes, rows);

        return
        [
            new SqliteParameter(parameter, SqliteType.Text)
            {
                Value = _Json(writer =>
                {
                    foreach (var row in rows)
                    {
                        writer.WriteStartArray();

                        foreach (var value in row)
                        {
                            _WriteElement(writer, value);
                        }

                        writer.WriteEndArray();
                    }
                }),
            },
        ];
    }

    public string Render(SqlLockedRead statement)
    {
        // The no-op write takes the database write lock now, as the other engines take the row lock here; inside a
        // transaction begun IMMEDIATE (the driver's default) it is already held and this changes nothing.
        var column = statement.Key[0].Column;

        return $"""
            UPDATE {statement.Table} SET {column} = {column} WHERE 0;
            SELECT {string.Join(", ", statement.Columns)}
            FROM {statement.Table}
            WHERE {_Key(statement.Key, alias: null)};
            """;
    }

    public string Render(SqlFencedTransition statement)
    {
        var fence = _Clocked(statement.Fence);

        if (statement.Set is null)
        {
            return $"""
                SELECT EXISTS (SELECT 1 FROM {statement.Table} WHERE {_Key(statement.Key, alias: null)} AND ({fence}));
                """;
        }

        // The UPDATE is its own guard: its WHERE carries the fence, so the check and the write are one decision on one
        // clock value. changes() in the next statement reports it.
        return $"""
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

        return $"""
            INSERT INTO {statement.Table} ({string.Join(", ", columns)})
            SELECT {string.Join(", ", values)}
            WHERE NOT EXISTS (SELECT 1 FROM {statement.Table} WHERE {_Key(statement.Key, alias: null)});
            {_AppliedAndReturning(statement.Table, statement.Key, statement.Returning)}
            """;
    }

    public string Render(SqlClaimNext statement)
    {
        var keys = string.Join(", ", statement.KeyColumns);

        return $"""
            UPDATE {statement.Table}
            SET {_Clocked(statement.Set)}
            WHERE ({keys}) IN (
                SELECT {keys}
                FROM {statement.Table}
                WHERE {_Clocked(statement.Filter)}
                ORDER BY {string.Join(", ", statement.OrderBy)}
                LIMIT {(statement.BatchSizeParameter is null ? "1" : "@" + statement.BatchSizeParameter)}
            )
            RETURNING {string.Join(", ", statement.Returning)};
            """;
    }

    public string Render(SqlDeleteBatch statement)
    {
        var keys = string.Join(", ", statement.KeyColumns);

        return $"""
            DELETE FROM {statement.Table}
            WHERE ({keys}) IN (
                SELECT {keys}
                FROM {statement.Table}
                WHERE {_Clocked(statement.Filter)}
                LIMIT @{statement.BatchSizeParameter}
            );
            """;
    }

    public string Render(SqlUpsert statement)
    {
        var columns = statement.Key.Select(static k => k.Column).Concat(statement.Columns);
        var values = statement.Key.Select(static k => "@" + k.Parameter).Concat(statement.Values.Select(_Clocked));
        var guard = statement.Guard is null ? "" : $" AND ({_Stored(_Clocked(statement.Guard))})";
        var returning = statement.Returning.Count == 0 ? "" : ", " + string.Join(", ", statement.Returning);

        // RETURNING cannot tell an upsert's insert from its update, so the two branches are separate statements, each
        // reporting its own outcome, and the decision row is the first row any of them returns: the update's, else the
        // insert's, else the refusal. The write lock the transaction holds keeps another writer out between them.
        return $"""
            UPDATE {statement.Table} AS stored
            SET {_Stored(_Clocked(statement.Set))}
            WHERE {_Key(statement.Key, "stored")}{guard}
            RETURNING {(int)SqlUpsertOutcome.Updated}{returning};
            INSERT INTO {statement.Table} ({string.Join(", ", columns)})
            SELECT {string.Join(", ", values)}
            WHERE NOT EXISTS (SELECT 1 FROM {statement.Table} WHERE {_Key(statement.Key, alias: null)})
            RETURNING {(int)SqlUpsertOutcome.Inserted}{returning};
            SELECT {(int)SqlUpsertOutcome.Refused};
            """;
    }

    public string Render(SqlClockedStatement statement)
    {
        return _Clocked(statement.Sql);
    }

    public SqlErrorKind Classify(Exception exception)
    {
        return exception switch
        {
            SqliteException e => e.SqliteErrorCode switch
            {
                SqliteErrors.Constraint
                    when e.SqliteExtendedErrorCode
                        is SqliteErrors.ConstraintPrimaryKey
                            or SqliteErrors.ConstraintUnique => SqlErrorKind.UniqueViolation,
                // The write lock was taken after this transaction's read snapshot: only a fresh transaction can write.
                SqliteErrors.Busy when e.SqliteExtendedErrorCode == SqliteErrors.BusySnapshot =>
                    SqlErrorKind.SerializationConflict,
                SqliteErrors.Busy or SqliteErrors.Locked => SqlErrorKind.LockTimeout,
                // SQLite reports an existing object only through the message of a generic error.
                SqliteErrors.Error when e.Message.Contains("already exists", StringComparison.Ordinal) =>
                    SqlErrorKind.DuplicateObject,
                _ => SqlErrorKind.None,
            },
            _ when SqliteErrors.IsCompletedTransaction(exception) => SqlErrorKind.TransactionAborted,
            _ => SqlErrorKind.None,
        };
    }

    /// <summary>Converts a bound value to the form its column stores.</summary>
    private static object? _ToStorage(object? value)
    {
        return value switch
        {
            null or DBNull => null,
            DateTimeOffset instant => _Instant(instant),
            // Upper-case text, the form EF Core's SQLite provider writes, so either can read the other's rows.
            Guid id => id.ToString("D").ToUpperInvariant(),
            bool flag => flag ? 1L : 0L,
            ReadOnlyMemory<byte> bytes => bytes.ToArray(),
            _ => value,
        };
    }

    private static string _Instant(DateTimeOffset instant)
    {
        return instant.UtcDateTime.ToString(_InstantFormat, CultureInfo.InvariantCulture) + _UtcSuffix;
    }

    private static void _WriteElement(Utf8JsonWriter writer, object? value)
    {
        switch (_ToStorage(value))
        {
            case string text:
                writer.WriteStringValue(text);

                break;
            case long number:
                writer.WriteNumberValue(number);

                break;
            case int number:
                writer.WriteNumberValue(number);

                break;
            case short number:
                writer.WriteNumberValue(number);

                break;
            case var other:
                throw new ArgumentException(
                    $"A list parameter cannot hold a {other?.GetType().Name ?? "null"} value.",
                    nameof(value)
                );
        }
    }

    private static string _Json(Action<Utf8JsonWriter> writeElements)
    {
        var buffer = new ArrayBufferWriter<byte>();

#pragma warning disable MA0045 // False positive: the writer flushes into an in-memory buffer, so there is no I/O to await.
        using (var writer = new Utf8JsonWriter(buffer))
#pragma warning restore MA0045
        {
            writer.WriteStartArray();
            writeElements(writer);
            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static SqliteType _StorageType(SqlColumnKind kind)
    {
        return kind switch
        {
            SqlColumnKind.KeyText
            or SqlColumnKind.Text
            or SqlColumnKind.Timestamp
            or SqlColumnKind.Guid
            or SqlColumnKind.Json => SqliteType.Text,
            SqlColumnKind.Int16 or SqlColumnKind.Int32 or SqlColumnKind.Int64 or SqlColumnKind.Boolean =>
                SqliteType.Integer,
            SqlColumnKind.Binary => SqliteType.Blob,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown column kind."),
        };
    }

    private static string _AppliedAndReturning(
        string table,
        IReadOnlyList<SqlKeyColumn> key,
        IReadOnlyList<string> returning
    )
    {
        // changes() reports the statement before this one. The re-read runs under the write lock the batch holds.
        var columns = returning.Count == 0 ? "" : ", " + string.Join(", ", returning.Select(static c => "t." + c));

        return $"""
            SELECT changes() > 0{columns}
            FROM (SELECT 1) AS x
            LEFT JOIN {table} AS t ON changes() > 0 AND {_Key(key, "t")};
            """;
    }

    private static string _Clocked(string fragment)
    {
        return fragment.Replace(SqlDialectTokens.Now, _Clock, StringComparison.Ordinal);
    }

    private static string _Stored(string fragment)
    {
        return fragment.Replace(SqlDialectTokens.Stored, "stored", StringComparison.Ordinal);
    }

    private static string _Key(IReadOnlyList<SqlKeyColumn> key, string? alias)
    {
        var prefix = alias is null ? "" : alias + ".";

        return string.Join(" AND ", key.Select(k => $"{prefix}{k.Column} = @{k.Parameter}"));
    }

    private static void _EnsureListable(SqlColumnType elementType)
    {
        if (elementType.Kind is SqlColumnKind.Binary or SqlColumnKind.Json)
        {
            throw new ArgumentException("A list parameter cannot hold binary or JSON values.", nameof(elementType));
        }
    }

    private static void _ValidateTuples(IReadOnlyList<string> expressions, IReadOnlyList<SqlColumnType> elementTypes)
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
            _EnsureListable(type);
        }
    }

    private static void _ValidateTupleRows(
        IReadOnlyList<SqlColumnType> elementTypes,
        IReadOnlyCollection<IReadOnlyList<object>> rows
    )
    {
        foreach (var type in elementTypes)
        {
            _EnsureListable(type);
        }

        foreach (var row in rows)
        {
            if (row.Count != elementTypes.Count || row.Any(static value => value is null))
            {
                throw new ArgumentException("Every tuple row needs one non-null value per element type.", nameof(rows));
            }
        }
    }
}
