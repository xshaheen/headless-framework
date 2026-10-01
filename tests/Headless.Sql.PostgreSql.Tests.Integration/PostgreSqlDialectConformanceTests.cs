// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sql;
using Headless.Sql.PostgreSql;
using Tests.TestSetup;

namespace Tests;

[Collection<NpgsqlTestFixture>]
public sealed class PostgreSqlDialectConformanceTests(NpgsqlTestFixture fixture) : SqlDialectConformanceTests
{
    protected override ISqlDialect Dialect => PostgreSqlDialect.Instance;

    protected override string ConnectionString => fixture.Container.GetConnectionString();

    protected override string CreateTableSql(string schema, string table)
    {
        return $"""
            CREATE SCHEMA IF NOT EXISTS "{schema}";
            CREATE TABLE "{schema}"."{table}" (
                key varchar(64) COLLATE "C" PRIMARY KEY,
                value bigint NOT NULL,
                flag boolean NULL,
                id uuid NULL,
                stamp timestamptz NULL,
                owner varchar(64) NULL
            );
            """;
    }

    protected override string CreatePartialKeySql(string table, string index)
    {
        return $"""CREATE UNIQUE INDEX "{index}" ON {table} (owner) WHERE flag = TRUE;""";
    }

    // An error aborts the whole transaction; every later statement on it fails with 25P02.
    protected override IReadOnlyList<string> DoomThenWriteBatches(string insertAfter)
    {
        return ["SELECT 1 / 0", insertAfter];
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
