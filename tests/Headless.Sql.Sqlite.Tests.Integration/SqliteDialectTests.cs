// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Sql;
using Headless.Sql.Sqlite;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Data.Sqlite;

namespace Tests;

/// <summary>
/// What SQLite's dialect does that the shared conformance suite cannot see: its text instants and their arithmetic,
/// and the database write lock it uses in place of row locks.
/// </summary>
public sealed class SqliteDialectTests : TestBase
{
    private static readonly SqliteDialect _Dialect = SqliteDialect.Instance;
    private readonly SqliteTestDatabase _database = SqliteTestDatabase.Create();

    public static TheoryData<DateTimeOffset, long> Shifts =>
        new()
        {
            { new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.Zero).AddTicks(1_234_560), 1 },
            { new DateTimeOffset(2026, 10, 1, 23, 59, 59, TimeSpan.Zero).AddTicks(9_999_990), 10 },
            { DateTimeOffset.UnixEpoch.AddTicks(10), -2_000_000 },
            { new DateTimeOffset(1969, 12, 31, 23, 59, 59, TimeSpan.Zero).AddTicks(5_000_000), 7_500_000 },
            { new DateTimeOffset(2000, 2, 28, 12, 0, 0, TimeSpan.FromHours(-5)), TimeSpan.FromDays(10_000).Ticks / 10 },
        };

    [Theory]
    [MemberData(nameof(Shifts))]
    public async Task should_shift_an_instant_by_a_duration_to_the_microsecond(DateTimeOffset instant, long micros)
    {
        var duration = TimeSpan.FromMicroseconds(micros);

        var forward = await _ShiftAsync(instant, duration, subtract: false);
        var backward = await _ShiftAsync(instant, duration, subtract: true);

        forward.Should().Be(instant.ToUniversalTime() + duration);
        backward.Should().Be(instant.ToUniversalTime() - duration);
    }

    [Fact]
    public async Task should_store_instants_so_text_order_is_instant_order()
    {
        await _ExecuteAsync("CREATE TABLE stamps (id INTEGER PRIMARY KEY, stamp TEXT NOT NULL)");
        var origin = new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.Zero);
        DateTimeOffset[] instants =
        [
            origin.AddTicks(10),
            origin.ToOffset(TimeSpan.FromHours(3)),
            origin.AddSeconds(-1),
            origin.AddMilliseconds(1),
            origin.AddTicks(1_000_000 - 10),
            new DateTimeOffset(1969, 7, 20, 20, 17, 40, TimeSpan.Zero),
        ];

        foreach (var instant in instants)
        {
            await _ExecuteAsync(
                "INSERT INTO stamps (stamp) VALUES (@s)",
                command => _Dialect.AddParameter(command, "s", SqlColumnType.Timestamp, instant)
            );
        }

        var read = new List<DateTimeOffset>();
        await using var connection = await _OpenAsync();
        await using var select = connection.CreateCommand();
        select.CommandText = "SELECT stamp FROM stamps ORDER BY stamp";
        await using var reader = await select.ExecuteReaderAsync(AbortToken);

        while (await reader.ReadAsync(AbortToken))
        {
            read.Add(await reader.GetFieldValueAsync<DateTimeOffset>(0, AbortToken));
        }

        read.Should().Equal(instants.Select(i => i.ToUniversalTime()).Order());
    }

    [Fact]
    public async Task should_read_the_clock_only_after_the_previous_writer_released_the_database()
    {
        await _ExecuteAsync("CREATE TABLE stamps (id INTEGER PRIMARY KEY, stamp TEXT NOT NULL)");
        var released = DateTimeOffset.MinValue;

        await using var holder = await _OpenAsync();
        await using (var holding = await holder.BeginTransactionAsync(AbortToken))
        {
            var waiter = Task.Run(
                async () =>
                {
                    await using var connection = await _OpenAsync();
                    await using var transaction = await connection.BeginTransactionAsync(AbortToken);
                    await using var command = connection.CreateCommand();
                    command.Transaction = (SqliteTransaction)transaction;
                    command.CommandText = _Dialect.Render(
                        new SqlClockedStatement($"INSERT INTO stamps (stamp) VALUES ({SqlDialectTokens.Now})")
                    );
                    await command.ExecuteNonQueryAsync(AbortToken);
                    await transaction.CommitAsync(AbortToken);
                },
                AbortToken
            );

            await Task.Delay(TimeSpan.FromMilliseconds(300), AbortToken);
            waiter.IsCompleted.Should().BeFalse("the waiter's transaction cannot begin while another holds the lock");
            released = DateTimeOffset.UtcNow;
            await holding.CommitAsync(AbortToken);
            await waiter;
        }

        var stamp = await _ScalarAsync<DateTimeOffset>("SELECT stamp FROM stamps");

        // SQLite's clock has millisecond resolution; the stamp can trail the host reading by less than that.
        stamp.Should().BeOnOrAfter(released.AddMilliseconds(-1), "the clock is read after the lock wait");
    }

    [Fact]
    public async Task should_take_the_write_lock_in_a_locked_read_of_a_deferred_transaction()
    {
        await _ExecuteAsync("CREATE TABLE items (key TEXT NOT NULL PRIMARY KEY, value INTEGER NOT NULL)");
        var read = _Dialect.Render(new SqlLockedRead("items", [new SqlKeyColumn("key", "k")], ["value"]));

        await using var reader = await _OpenAsync();
        await using var deferred = reader.BeginTransaction(deferred: true);
        await using (var command = reader.CreateCommand())
        {
            command.Transaction = deferred;
            command.CommandText = read;
            _Dialect.AddParameter(command, "k", SqlColumnType.KeyText(64), "absent");
            await using var result = await command.ExecuteReaderAsync(AbortToken);
            (await result.ReadAsync(AbortToken)).Should().BeFalse();
        }

        await using var writer = new SqliteConnection(_database.ConnectionString + ";Default Timeout=1");
        await writer.OpenAsync(AbortToken);
        var waited = Stopwatch.StartNew();
        var act = async () => await writer.BeginTransactionAsync(AbortToken);

        var busy = (await act.Should().ThrowAsync<SqliteException>()).Which;

        waited.Elapsed.Should().BeGreaterThan(TimeSpan.FromMilliseconds(500), "the writer waited out its busy timeout");
        _Dialect.Classify(busy).Should().Be(SqlErrorKind.LockTimeout);
        RelationalTransientFaults.IsTransient(busy, AbortToken).Should().BeTrue("a fresh transaction can clear it");
    }

    [Fact]
    public async Task should_classify_a_duplicate_key_as_a_unique_violation_and_not_transient()
    {
        await _ExecuteAsync("CREATE TABLE items (key TEXT NOT NULL PRIMARY KEY)");
        await _ExecuteAsync("INSERT INTO items VALUES ('k')");

        var act = () => _ExecuteAsync("INSERT INTO items VALUES ('k')");

        var duplicate = (await act.Should().ThrowAsync<SqliteException>()).Which;
        _Dialect.Classify(duplicate).Should().Be(SqlErrorKind.UniqueViolation);
        RelationalTransientFaults.IsTransient(duplicate, AbortToken).Should().BeFalse();
    }

    [Fact]
    public async Task should_prefix_the_schema_onto_qualified_names()
    {
        var table = _Dialect.Qualify("headless", "fencing_leases");
        await _ExecuteAsync($"CREATE TABLE {table} (id INTEGER PRIMARY KEY)");

        table.Should().Be("\"headless_fencing_leases\"");
        (await _ScalarAsync<long>("SELECT count(*) FROM sqlite_schema WHERE name = 'headless_fencing_leases'"))
            .Should()
            .Be(1);
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        await _database.DisposeAsync();
        await base.DisposeAsyncCore();
    }

    private async Task<DateTimeOffset> _ShiftAsync(DateTimeOffset instant, TimeSpan duration, bool subtract)
    {
        await using var connection = await _OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {_Dialect.ShiftByDuration("@at", "by", subtract)}";
        _Dialect.AddParameter(command, "at", SqlColumnType.Timestamp, instant);
        _Dialect.AddDuration(command, "by", duration);
        await using var reader = await command.ExecuteReaderAsync(AbortToken);
        await reader.ReadAsync(AbortToken);

        return await reader.GetFieldValueAsync<DateTimeOffset>(0, AbortToken);
    }

    private async Task<T> _ScalarAsync<T>(string sql)
    {
        await using var connection = await _OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(AbortToken);
        await reader.ReadAsync(AbortToken);

        return await reader.GetFieldValueAsync<T>(0, AbortToken);
    }

    private async Task _ExecuteAsync(string sql, Action<SqliteCommand>? bind = null)
    {
        await using var connection = await _OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        bind?.Invoke(command);
        await command.ExecuteNonQueryAsync(AbortToken);
    }

    private async Task<SqliteConnection> _OpenAsync()
    {
        var connection = new SqliteConnection(_database.ConnectionString);
        await connection.OpenAsync(AbortToken);

        return connection;
    }
}
