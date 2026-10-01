// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using System.Globalization;
using Headless.Sql;
using Headless.Testing.Tests;

namespace Tests;

/// <summary>
/// Runs every dialect statement shape against a real engine, with the same assertions on each, so the two dialects
/// cannot disagree on what a shape does.
/// </summary>
public abstract class SqlDialectConformanceTests : TestBase
{
    private int _table;

    protected abstract ISqlDialect Dialect { get; }

    protected abstract string ConnectionString { get; }

    /// <summary>Creates <paramref name="table" /> with the key, value, flag, id, stamp, and owner columns.</summary>
    protected abstract string CreateTableSql(string schema, string table);

    /// <summary>
    /// The batches that fail inside a transaction and then write <paramref name="insertAfter" /> on it, in the shape
    /// the engine dooms rather than rolls back: the last batch's write must be refused.
    /// </summary>
    protected abstract IReadOnlyList<string> DoomThenWriteBatches(string insertAfter);

    /// <summary>
    /// Creates a unique index named <paramref name="index" /> over the owner column of <paramref name="table" />,
    /// covering only rows whose flag is set and written with the dialect's boolean literal, as a partial key.
    /// </summary>
    protected abstract string CreatePartialKeySql(string table, string index);

    private string _Column(string pascal) => Dialect.Quote(Dialect.Name(pascal));

    public virtual async Task should_insert_then_update_and_report_which()
    {
        var table = await _CreateTableAsync();

        var first = await _UpsertAsync(table, "k", increment: 1, guardBelow: null);
        var second = await _UpsertAsync(table, "k", increment: 1, guardBelow: null);

        first
            .Match((value, outcome) => (outcome, value), () => (SqlUpsertOutcome.Refused, -1L))
            .Should()
            .Be((SqlUpsertOutcome.Inserted, 1L));
        second
            .Match((value, outcome) => (outcome, value), () => (SqlUpsertOutcome.Refused, -1L))
            .Should()
            .Be((SqlUpsertOutcome.Updated, 2L));
    }

    public virtual async Task should_refuse_an_update_the_guard_rejects_and_write_nothing()
    {
        var table = await _CreateTableAsync();
        _ = await _UpsertAsync(table, "k", increment: 5, guardBelow: null);

        var refused = await _UpsertAsync(table, "k", increment: 1, guardBelow: 5);

        refused.Outcome.Should().Be(SqlUpsertOutcome.Refused);
        (await _ReadValueAsync(table, "k")).Should().Be(5);
    }

    public virtual async Task should_insert_exactly_once_when_racers_upsert_one_key()
    {
        const int racers = 16;
        var table = await _CreateTableAsync();

        var outcomes = await Task.WhenAll(
            Enumerable.Range(0, racers).Select(_ => Task.Run(() => _UpsertAsync(table, "hot", 1, null), AbortToken))
        );

        outcomes.Count(o => o.Outcome == SqlUpsertOutcome.Inserted).Should().Be(1);
        outcomes.Count(o => o.Outcome == SqlUpsertOutcome.Updated).Should().Be(racers - 1);
        (await _ReadValueAsync(table, "hot")).Should().Be(racers);
    }

    public virtual async Task should_claim_at_most_the_batch_and_never_the_same_row_twice()
    {
        var table = await _CreateTableAsync();

        for (var i = 0; i < 10; i++)
        {
            _ = await _UpsertAsync(table, $"row-{i:D2}", increment: i, guardBelow: null);
        }

        await using var first = await _OpenAsync();
        await using var second = await _OpenAsync();
        await using var firstTransaction = await first.BeginTransactionAsync(AbortToken);
        await using var secondTransaction = await second.BeginTransactionAsync(AbortToken);

        var claimedByFirst = await _ClaimAsync(first, firstTransaction, table, "a", batch: 4);
        var claimedBySecond = await _ClaimAsync(second, secondTransaction, table, "b", batch: 4);

        claimedByFirst.Should().HaveCount(4);
        claimedBySecond.Should().HaveCount(4);
        claimedByFirst.Should().NotIntersectWith(claimedBySecond, "a claim skips rows another claim has locked");
        claimedByFirst.Should().BeEquivalentTo(["row-00", "row-01", "row-02", "row-03"]);
    }

    public virtual async Task should_match_list_parameters_of_every_kind_and_nothing_for_an_empty_list()
    {
        var table = await _CreateTableAsync();
        var id = Guid.NewGuid();
        var stamp = new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.FromHours(3));
        await _ExecuteAsync(
            $"INSERT INTO {table} ({_Column("Key")}, {_Column("Value")}, {_Column("Flag")}, {_Column("Id")}, {_Column("Stamp")}) VALUES (@k, @v, @f, @i, @s)",
            command =>
            {
                Dialect.AddParameter(command, "k", SqlColumnType.KeyText(64), "listed");
                Dialect.AddParameter(command, "v", SqlColumnType.Int64, 42L);
                Dialect.AddParameter(command, "f", SqlColumnType.Boolean, true);
                Dialect.AddParameter(command, "i", SqlColumnType.Guid, id);
                Dialect.AddParameter(command, "s", SqlColumnType.Timestamp, stamp);
            }
        );

        (await _CountWhereInAsync(table, "Key", SqlColumnType.KeyText(64), new[] { "other", "listed" })).Should().Be(1);
        (await _CountWhereInAsync(table, "Value", SqlColumnType.Int64, new[] { 1L, 42L })).Should().Be(1);
        (await _CountWhereInAsync(table, "Flag", SqlColumnType.Boolean, new[] { true })).Should().Be(1);
        (await _CountWhereInAsync(table, "Id", SqlColumnType.Guid, new[] { Guid.NewGuid(), id })).Should().Be(1);
        (await _CountWhereInAsync(table, "Stamp", SqlColumnType.Timestamp, new[] { stamp.ToUniversalTime() }))
            .Should()
            .Be(1, "the same instant matches whatever offset it is written with");
        (await _CountWhereInAsync(table, "Key", SqlColumnType.KeyText(64), Array.Empty<string>())).Should().Be(0);
    }

    public virtual async Task should_match_tuple_lists_row_by_row_and_never_across_rows()
    {
        var table = await _CreateTableAsync();
        var id = Guid.NewGuid();
        var stamp = new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.Zero);
        _ = await _UpsertAsync(table, "a", increment: 1, guardBelow: null);
        _ = await _UpsertAsync(table, "b", increment: 2, guardBelow: null);
        await _ExecuteAsync(
            $"UPDATE {table} SET {_Column("Id")} = @i, {_Column("Stamp")} = @s WHERE {_Column("Key")} = 'a'",
            command =>
            {
                Dialect.AddParameter(command, "i", SqlColumnType.Guid, id);
                Dialect.AddParameter(command, "s", SqlColumnType.Timestamp, stamp);
            }
        );
        SqlColumnType[] keyAndValue = [SqlColumnType.KeyText(64), SqlColumnType.Int64];

        // ("a", 2) must not match: "a" holds 1, and only "b" holds 2.
        (
            await _CountWhereInTuplesAsync(
                table,
                ["Key", "Value"],
                keyAndValue,
                [
                    ["a", 2L],
                    ["b", 2L],
                ]
            )
        )
            .Should()
            .Be(1);
        (
            await _CountWhereInTuplesAsync(
                table,
                ["Key", "Value"],
                keyAndValue,
                [
                    ["a", 1L],
                    ["b", 2L],
                ]
            )
        ).Should().Be(2);
        (
            await _CountWhereInTuplesAsync(
                table,
                ["Id", "Stamp"],
                [SqlColumnType.Guid, SqlColumnType.Timestamp],
                [
                    [id, stamp.ToOffset(TimeSpan.FromHours(2))],
                ]
            )
        ).Should().Be(1, "the same instant matches whatever offset it is written with");
        (await _CountWhereInTuplesAsync(table, ["Key", "Value"], keyAndValue, [])).Should().Be(0);
    }

    public virtual async Task should_delete_by_the_database_clock_in_a_clocked_statement()
    {
        var table = await _CreateTableAsync();
        await _ExecuteAsync(
            Dialect.Render(
                new SqlClockedStatement(
                    $"INSERT INTO {table} ({_Column("Key")}, {_Column("Value")}, {_Column("Stamp")}) VALUES ('past', 0, {Dialect.ShiftByDuration(SqlDialectTokens.Now, "Age", subtract: true)}), ('future', 0, {Dialect.ShiftByDuration(SqlDialectTokens.Now, "Age")})"
                )
            ),
            command => Dialect.AddDuration(command, "Age", TimeSpan.FromHours(1))
        );

        await _ExecuteAsync(
            Dialect.Render(
                new SqlClockedStatement($"DELETE FROM {table} WHERE {_Column("Stamp")} < {SqlDialectTokens.Now}")
            ),
            _ => { }
        );

        (await _ReadValueAsync(table, "past")).Should().BeNull();
        (await _ReadValueAsync(table, "future")).Should().Be(0);
    }

    public virtual async Task should_insert_and_report_the_values_it_wrote()
    {
        var table = await _CreateTableAsync();
        await using var connection = await _OpenAsync();
        var before = await _ReadDatabaseNowAsync(connection);

        await using var command = connection.CreateCommand();
        command.CommandText = Dialect.Render(
            new SqlInsert(
                table,
                [_Column("Key"), _Column("Value"), _Column("Stamp")],
                ["@k", "@v", SqlDialectTokens.Now],
                [_Column("Key"), _Column("Stamp")]
            )
        );
        Dialect.AddParameter(command, "k", SqlColumnType.KeyText(64), "written");
        Dialect.AddParameter(command, "v", SqlColumnType.Int64, 7L);
        await using var reader = await command.ExecuteReaderAsync(AbortToken);

        (await reader.ReadAsync(AbortToken)).Should().BeTrue();
        reader.GetString(0).Should().Be("written");
        (await reader.GetFieldValueAsync<DateTimeOffset>(1, AbortToken))
            .Should()
            .BeOnOrAfter(before, "the stamp is the database clock read by the insert");
        (await reader.ReadAsync(AbortToken)).Should().BeFalse();
    }

    public virtual async Task should_lock_a_batch_in_order_and_skip_rows_another_transaction_holds()
    {
        var table = await _CreateTableAsync();

        for (var i = 0; i < 6; i++)
        {
            _ = await _UpsertAsync(table, $"row-{i:D2}", increment: i, guardBelow: null);
        }

        await using var first = await _OpenAsync();
        await using var second = await _OpenAsync();
        await using var firstTransaction = await first.BeginTransactionAsync(AbortToken);
        await using var secondTransaction = await second.BeginTransactionAsync(AbortToken);

        var lockedByFirst = await _LockBatchAsync(first, firstTransaction, table, batch: 2);
        var lockedBySecond = await _LockBatchAsync(second, secondTransaction, table, batch: 2);

        lockedByFirst.Should().Equal("row-00", "row-01");
        lockedBySecond.Should().Equal(["row-02", "row-03"], "a locked batch skips rows another transaction holds");
    }

    public virtual async Task should_make_holders_of_one_transaction_lock_wait_in_turn()
    {
        var resource = $"conformance.{Guid.NewGuid():N}";
        await using var first = await _OpenAsync();
        await using var firstTransaction = await first.BeginTransactionAsync(AbortToken);
        await _TakeTransactionLockAsync(first, firstTransaction, resource);

        var waiting = _TakeTransactionLockInOwnTransactionAsync(resource);
        await Task.Delay(TimeSpan.FromMilliseconds(500), AbortToken);
        waiting.IsCompleted.Should().BeFalse("the lock is held until the first transaction ends");

        await firstTransaction.CommitAsync(AbortToken);
        await waiting;
    }

    public virtual async Task should_render_portable_expressions()
    {
        var table = await _CreateTableAsync();
        var stamp = new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.Zero);
        await _ExecuteAsync(
            $"INSERT INTO {table} ({_Column("Key")}, {_Column("Value")}, {_Column("Flag")}, {_Column("Stamp")}, {_Column("Owner")}) VALUES ('a', 1, {Dialect.BooleanLiteral(true)}, @s, 'Hello World'), ('b', 2, {Dialect.BooleanLiteral(false)}, @s, '100% done'), ('c', 3, NULL, @s, NULL)",
            command => Dialect.AddParameter(command, "s", SqlColumnType.Timestamp, stamp)
        );

        (
            await _ScalarAsync(
                $"SELECT COUNT(*) FROM {table} WHERE {_Column("Flag")} = {Dialect.BooleanLiteral(true)}",
                _ => { }
            )
        )
            .Should()
            .Be(1L);
        (
            await _ScalarAsync(
                $"SELECT COUNT(*) FROM {table} WHERE {_Column("Flag")} = {Dialect.BooleanLiteral(false)}",
                _ => { }
            )
        )
            .Should()
            .Be(1L);

        await _ExecuteAsync($"UPDATE {table} SET {_Column("Id")} = {Dialect.NewGuid()}", _ => { });
        (await _ScalarAsync($"SELECT COUNT(DISTINCT {_Column("Id")}) FROM {table}", _ => { }))
            .Should()
            .Be(3L, "every row draws its own identifier");

        await using (var connection = await _OpenAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                $"SELECT {Dialect.ShiftBySeconds(_Column("Stamp"), _Column("Value"))} FROM {table} WHERE {_Column("Key")} = 'b'";
            (await command.ExecuteScalarAsync(AbortToken) is { } shifted ? _Instant(shifted) : default)
                .Should()
                .Be(stamp.AddSeconds(2));
        }

        (
            await _KeysAsync(
                table,
                Dialect.Limit("l", "o"),
                command =>
                {
                    Dialect.AddParameter(command, "l", SqlColumnType.Int32, 1);
                    Dialect.AddParameter(command, "o", SqlColumnType.Int64, 1L);
                }
            )
        ).Should().Equal("b");
        (
            await _KeysAsync(
                table,
                Dialect.Limit("l"),
                command => Dialect.AddParameter(command, "l", SqlColumnType.Int32, 2)
            )
        )
            .Should()
            .Equal("a", "b");

        (
            await _ScalarAsync(
                $"SELECT COUNT(*) FROM {table} WHERE {Dialect.LikeIgnoringCase(_Column("Owner"), "p")}",
                command => Dialect.AddParameter(command, "p", SqlColumnType.Text(64), "%WORLD%")
            )
        )
            .Should()
            .Be(1L, "the match ignores case");
        (
            await _ScalarAsync(
                $"SELECT COUNT(*) FROM {table} WHERE {Dialect.LikeIgnoringCase(_Column("Owner"), "p")}",
                command => Dialect.AddParameter(command, "p", SqlColumnType.Text(64), "%0\\%%")
            )
        )
            .Should()
            .Be(1L, "a backslash escapes a wildcard");

        byte[] bytes = [1, 2, 3, 4];
        (
            await _ScalarObjectAsync(
                "SELECT @b",
                command => Dialect.AddParameter(command, "b", SqlColumnType.FixedBinary(4), bytes)
            )
        )
            .Should()
            .BeEquivalentTo(bytes);
    }

    public virtual async Task should_read_without_waiting_on_a_row_another_transaction_holds()
    {
        var table = await _CreateTableAsync();
        _ = await _UpsertAsync(table, "held", increment: 1, guardBelow: null);
        await using var holder = await _OpenAsync();
        await using var transaction = await holder.BeginTransactionAsync(AbortToken);
        await using (var update = holder.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = $"UPDATE {table} SET {_Column("Value")} = {_Column("Value")} + 1";
            await update.ExecuteNonQueryAsync(AbortToken);
        }

        await using var reader = await _OpenAsync();
        await using var count = reader.CreateCommand();
        count.CommandText = $"SELECT COUNT(*) FROM {Dialect.ReadWithoutWaiting(table)}";
        count.CommandTimeout = 5;

        // PostgreSQL returns the row as last committed; SQL Server skips it. Neither waits for the holder.
        Convert
            .ToInt64(await count.ExecuteScalarAsync(AbortToken), CultureInfo.InvariantCulture)
            .Should()
            .BeInRange(0, 1);
    }

    public virtual async Task should_insert_and_lock_by_a_partial_key()
    {
        var table = await _CreateTableAsync();
        await _ExecuteAsync(CreatePartialKeySql(table, Dialect.Name($"UxOwner{Guid.NewGuid():N}")), _ => { });
        var flagged = $"{_Column("Flag")} = {Dialect.BooleanLiteral(true)}";
        var insert = Dialect.Render(
            new SqlInsertIfAbsent(
                table,
                [new SqlKeyColumn(_Column("Owner"), "o")],
                [_Column("Key"), _Column("Value"), _Column("Flag")],
                ["@k", "1", Dialect.BooleanLiteral(true)],
                [_Column("Key")],
                flagged
            )
        );

        // A row with the owner but without the flag is outside the partial key, so it does not hold it.
        await _ExecuteAsync(
            $"INSERT INTO {table} ({_Column("Key")}, {_Column("Value")}, {_Column("Flag")}, {_Column("Owner")}) VALUES ('plain', 0, {Dialect.BooleanLiteral(false)}, 'owner')",
            _ => { }
        );

        (await _InsertIfAbsentAsync(insert, "first", "owner")).Should().BeTrue();
        (await _InsertIfAbsentAsync(insert, "second", "owner")).Should().BeFalse("the partial key is held");

        await using var connection = await _OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync(AbortToken);
        await using var read = connection.CreateCommand();
        read.Transaction = transaction;
        read.CommandText = Dialect.Render(
            new SqlLockedRead(table, [new SqlKeyColumn(_Column("Owner"), "o")], [_Column("Key")], flagged)
        );
        Dialect.AddParameter(read, "o", SqlColumnType.Text(64), "owner");
        var keys = new List<string>();
        await using (var reader = await read.ExecuteReaderAsync(AbortToken))
        {
            while (await reader.ReadAsync(AbortToken))
            {
                keys.Add(reader.GetString(0));
            }
        }

        keys.Should().Equal("first");
    }

    public virtual async Task should_fail_loudly_once_the_engine_has_doomed_the_callers_transaction()
    {
        var table = await _CreateTableAsync();
        var insert = $"INSERT INTO {table} ({_Column("Key")}, {_Column("Value")}) VALUES ('after', 1)";
        var batches = DoomThenWriteBatches(insert);
        DbException? last = null;

        await using (var connection = await _OpenAsync())
        {
            await using var transaction = await connection.BeginTransactionAsync(AbortToken);

            foreach (var batch in batches)
            {
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = batch;

                try
                {
                    await command.ExecuteNonQueryAsync(AbortToken);
                    last = null;
                }
                catch (DbException e)
                {
                    last = e;
                }
            }
        }

        // The write after the failure must be refused, never run in autocommit and report success.
        last.Should().NotBeNull("the write ran on a transaction the engine had already doomed");
        Dialect.Classify(last!).Should().Be(SqlErrorKind.TransactionAborted);
        (await _ReadValueAsync(table, "after")).Should().BeNull();
    }

    private async Task<SqlUpserted<long>> _UpsertAsync(string table, string key, long increment, long? guardBelow)
    {
        var value = _Column("Value");
        var statement = new SqlUpsert(
            table,
            [new SqlKeyColumn(_Column("Key"), "k")],
            [value, _Column("Stamp")],
            ["@inc", SqlDialectTokens.Now],
            $"{value} = {SqlDialectTokens.Stored}.{value} + @inc, {_Column("Stamp")} = {SqlDialectTokens.Now}",
            guardBelow is null ? null : $"{SqlDialectTokens.Stored}.{value} < @limit",
            [value]
        );

        await using var connection = await _OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync(AbortToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = Dialect.Render(statement);
        Dialect.AddParameter(command, "k", SqlColumnType.KeyText(64), key);
        Dialect.AddParameter(command, "inc", SqlColumnType.Int64, increment);

        if (guardBelow is not null)
        {
            Dialect.AddParameter(command, "limit", SqlColumnType.Int64, guardBelow);
        }

        var result = await SqlUpsertCommand.ExecuteAsync(
            command,
            static (reader, _) => ValueTask.FromResult(reader.GetInt64(1)),
            AbortToken
        );
        await transaction.CommitAsync(AbortToken);

        return result;
    }

    private async Task<List<string>> _ClaimAsync(
        DbConnection connection,
        DbTransaction transaction,
        string table,
        string owner,
        int batch
    )
    {
        var statement = new SqlClaimNext(
            table,
            [_Column("Key")],
            $"{_Column("Owner")} IS NULL",
            [_Column("Key")],
            $"{_Column("Owner")} = @owner",
            [_Column("Key")],
            BatchSizeParameter: "batch"
        );

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = Dialect.Render(statement);
        Dialect.AddParameter(command, "owner", SqlColumnType.Text(64), owner);
        Dialect.AddParameter(command, "batch", SqlColumnType.Int32, batch);

        var claimed = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(AbortToken);

        while (await reader.ReadAsync(AbortToken))
        {
            claimed.Add(reader.GetString(0));
        }

        return claimed;
    }

    private async Task<List<string>> _LockBatchAsync(
        DbConnection connection,
        DbTransaction transaction,
        string table,
        int batch
    )
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = Dialect.Render(
            new SqlLockBatch(table, [_Column("Key")], $"{_Column("Value")} >= 0", [_Column("Key")], "batch")
        );
        Dialect.AddParameter(command, "batch", SqlColumnType.Int32, batch);

        var locked = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(AbortToken);

        while (await reader.ReadAsync(AbortToken))
        {
            locked.Add(reader.GetString(0));
        }

        return locked;
    }

    private async Task _TakeTransactionLockAsync(DbConnection connection, DbTransaction transaction, string resource)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = Dialect.Render(new SqlTransactionLock("r"));
        Dialect.AddParameter(command, "r", SqlColumnType.Text(255), resource);
        await command.ExecuteNonQueryAsync(AbortToken);
    }

    private async Task _TakeTransactionLockInOwnTransactionAsync(string resource)
    {
        await using var connection = await _OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync(AbortToken);
        await _TakeTransactionLockAsync(connection, transaction, resource);
        await transaction.CommitAsync(AbortToken);
    }

    private async Task<bool> _InsertIfAbsentAsync(string sql, string key, string owner)
    {
        await using var connection = await _OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        Dialect.AddParameter(command, "k", SqlColumnType.KeyText(64), key);
        Dialect.AddParameter(command, "o", SqlColumnType.Text(64), owner);
        await using var reader = await command.ExecuteReaderAsync(AbortToken);

        return await reader.ReadAsync(AbortToken) && reader.GetBoolean(0);
    }

    private async Task<List<string>> _KeysAsync(string table, string limit, Action<DbCommand> bind)
    {
        await using var connection = await _OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {_Column("Key")} FROM {table} ORDER BY {_Column("Key")} {limit}";
        bind(command);

        var keys = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(AbortToken);

        while (await reader.ReadAsync(AbortToken))
        {
            keys.Add(reader.GetString(0));
        }

        return keys;
    }

    private async Task<long> _ScalarAsync(string sql, Action<DbCommand> bind)
    {
        return Convert.ToInt64(await _ScalarObjectAsync(sql, bind), CultureInfo.InvariantCulture);
    }

    private async Task<object?> _ScalarObjectAsync(string sql, Action<DbCommand> bind)
    {
        await using var connection = await _OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        bind(command);

        return await command.ExecuteScalarAsync(AbortToken);
    }

    private async Task<DateTimeOffset> _ReadDatabaseNowAsync(DbConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = Dialect.Render(new SqlClockedStatement($"SELECT {SqlDialectTokens.Now}"));

        return _Instant((await command.ExecuteScalarAsync(AbortToken))!);
    }

    /// <summary>A timestamp as the driver returns it: Npgsql reads timestamptz as a UTC <see cref="DateTime" />.</summary>
    private static DateTimeOffset _Instant(object value)
    {
        return value switch
        {
            DateTimeOffset instant => instant,
            DateTime utc => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)),
            _ => throw new InvalidOperationException($"Not a timestamp: {value.GetType()}."),
        };
    }

    private async Task<long> _CountWhereInAsync<T>(
        string table,
        string column,
        SqlColumnType type,
        IReadOnlyCollection<T> values
    )
    {
        await using var connection = await _OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table} WHERE {Dialect.InList(_Column(column), "list", type)}";
        Dialect.AddListParameter(command, "list", type, values);

        return Convert.ToInt64(await command.ExecuteScalarAsync(AbortToken), CultureInfo.InvariantCulture);
    }

    private async Task<long> _CountWhereInTuplesAsync(
        string table,
        IReadOnlyList<string> columns,
        IReadOnlyList<SqlColumnType> types,
        IReadOnlyCollection<IReadOnlyList<object>> rows
    )
    {
        await using var connection = await _OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT COUNT(*) FROM {table} WHERE {Dialect.InTuples([.. columns.Select(_Column)], "rows", types)}";
        command.Parameters.AddRange(Dialect.CreateTupleListParameters("rows", types, rows).ToArray());

        return Convert.ToInt64(await command.ExecuteScalarAsync(AbortToken), CultureInfo.InvariantCulture);
    }

    private async Task<long?> _ReadValueAsync(string table, string key)
    {
        await using var connection = await _OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {_Column("Value")} FROM {table} WHERE {_Column("Key")} = @k";
        Dialect.AddParameter(command, "k", SqlColumnType.KeyText(64), key);
        var value = await command.ExecuteScalarAsync(AbortToken);

        return value is null or DBNull ? null : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    private async Task _ExecuteAsync(string sql, Action<DbCommand> bind)
    {
        await using var connection = await _OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        bind(command);
        await command.ExecuteNonQueryAsync(AbortToken);
    }

    private async Task<string> _CreateTableAsync()
    {
        var schema = Dialect.Name("KitConformance");
        var name = Dialect.Name($"Items{Interlocked.Increment(ref _table)}{Guid.NewGuid():N}");
        await _ExecuteAsync(CreateTableSql(schema, name), _ => { });

        return Dialect.Qualify(schema, name);
    }

    private async Task<DbConnection> _OpenAsync()
    {
        var connection = Dialect.CreateConnection(ConnectionString);
        await connection.OpenAsync(AbortToken);

        return connection;
    }
}
