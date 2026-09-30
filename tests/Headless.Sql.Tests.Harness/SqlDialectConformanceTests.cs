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
