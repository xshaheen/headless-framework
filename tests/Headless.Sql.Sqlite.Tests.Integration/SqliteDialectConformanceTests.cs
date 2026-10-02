// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sql;
using Headless.Sql.Sqlite;

namespace Tests;

public sealed class SqliteDialectConformanceTests : SqlDialectConformanceTests
{
    private readonly SqliteTestDatabase _database = SqliteTestDatabase.Create();

    protected override ISqlDialect Dialect => SqliteDialect.Instance;

    protected override string ConnectionString => _database.ConnectionString;

    protected override bool SerializesWriteTransactions => true;

    protected override string CreateTableSql(string schema, string table)
    {
        return $"""
            CREATE TABLE {Dialect.Qualify(schema, table)} (
                key TEXT NOT NULL PRIMARY KEY,
                value INTEGER NOT NULL,
                flag INTEGER NULL,
                id TEXT NULL,
                stamp TEXT NULL,
                owner TEXT NULL
            );
            """;
    }

    protected override string CreatePartialKeySql(string table, string index)
    {
        return $"""CREATE UNIQUE INDEX "{index}" ON {table} (owner) WHERE flag = 1;""";
    }

    // A transaction begun IMMEDIATE holds the database write lock from its first statement, so two SQLite transactions
    // never hold one row each and wait on the other. The deadlock scenarios are not overridden below; throwing keeps a
    // future override from passing without ever forcing a deadlock.
    protected override string DeadlockSurvivorSql =>
        throw new NotSupportedException("SQLite serializes write transactions at begin, so it cannot deadlock.");

    // SQLite keeps a transaction alive after an ordinary error, but an OR ROLLBACK conflict (like a full disk or an I/O
    // error) rolls the whole transaction back, after which the driver refuses every statement on it.
    protected override IReadOnlyList<string> DoomThenWriteBatches(string insertAfter)
    {
        return
        [
            """
                CREATE TEMP TABLE doom (k INTEGER PRIMARY KEY);
                INSERT INTO doom VALUES (1);
                INSERT OR ROLLBACK INTO doom VALUES (1);
                """,
            insertAfter,
        ];
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        await _database.DisposeAsync();
        await base.DisposeAsyncCore();
    }

    [Fact]
    public override Task should_match_tuple_lists_row_by_row_and_never_across_rows()
    {
        return base.should_match_tuple_lists_row_by_row_and_never_across_rows();
    }

    [Fact]
    public override Task should_insert_then_update_and_report_which()
    {
        return base.should_insert_then_update_and_report_which();
    }

    [Fact]
    public override Task should_refuse_an_update_the_guard_rejects_and_write_nothing()
    {
        return base.should_refuse_an_update_the_guard_rejects_and_write_nothing();
    }

    [Fact]
    public override Task should_insert_exactly_once_when_racers_upsert_one_key()
    {
        return base.should_insert_exactly_once_when_racers_upsert_one_key();
    }

    [Fact]
    public override Task should_claim_at_most_the_batch_and_never_the_same_row_twice()
    {
        return base.should_claim_at_most_the_batch_and_never_the_same_row_twice();
    }

    [Fact]
    public override Task should_match_list_parameters_of_every_kind_and_nothing_for_an_empty_list()
    {
        return base.should_match_list_parameters_of_every_kind_and_nothing_for_an_empty_list();
    }

    [Fact]
    public override Task should_delete_by_the_database_clock_in_a_clocked_statement()
    {
        return base.should_delete_by_the_database_clock_in_a_clocked_statement();
    }

    [Fact]
    public override Task should_fail_loudly_once_the_engine_has_doomed_the_callers_transaction()
    {
        return base.should_fail_loudly_once_the_engine_has_doomed_the_callers_transaction();
    }

    [Fact]
    public override Task should_insert_and_report_the_values_it_wrote()
    {
        return base.should_insert_and_report_the_values_it_wrote();
    }

    [Fact]
    public override Task should_lock_a_batch_in_order_and_skip_rows_another_transaction_holds()
    {
        return base.should_lock_a_batch_in_order_and_skip_rows_another_transaction_holds();
    }

    [Fact]
    public override Task should_make_holders_of_one_transaction_lock_wait_in_turn()
    {
        return base.should_make_holders_of_one_transaction_lock_wait_in_turn();
    }

    [Fact]
    public override Task should_render_portable_expressions()
    {
        return base.should_render_portable_expressions();
    }

    [Fact]
    public override Task should_read_without_waiting_on_a_row_another_transaction_holds()
    {
        return base.should_read_without_waiting_on_a_row_another_transaction_holds();
    }

    [Fact]
    public override Task should_insert_and_lock_by_a_partial_key()
    {
        return base.should_insert_and_lock_by_a_partial_key();
    }
}
