// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sql;
using Headless.Sql.SqlServer;
using Tests.TestSetup;

namespace Tests;

[Collection<SqlServerTestFixture>]
public sealed class SqlServerDialectConformanceTests(SqlServerTestFixture fixture) : SqlDialectConformanceTests
{
    protected override ISqlDialect Dialect => SqlServerDialect.Instance;

    protected override string ConnectionString => fixture.ConnectionString;

    protected override string CreateTableSql(string schema, string table)
    {
        return $"""
            IF SCHEMA_ID(N'{schema}') IS NULL EXEC(N'CREATE SCHEMA [{schema}]');
            CREATE TABLE [{schema}].[{table}] (
                [Key] nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL PRIMARY KEY,
                [Value] bigint NOT NULL,
                [Flag] bit NULL,
                [Id] uniqueidentifier NULL,
                [Stamp] datetimeoffset(7) NULL,
                [Owner] nvarchar(64) NULL
            );
            """;
    }

    protected override string CreatePartialKeySql(string table, string index)
    {
        return $"CREATE UNIQUE INDEX [{index}] ON {table} ([Owner]) WHERE [Flag] = 1;";
    }

    // The deadlock monitor always picks the session with the lower DEADLOCK_PRIORITY as the victim.
    protected override string DeadlockSurvivorSql => "SET DEADLOCK_PRIORITY HIGH";

    // A TRY/CATCH that swallows an error under XACT_ABORT ON leaves the transaction doomed rather than rolled back, and
    // the rest of the batch keeps running on it: the write is refused with 3930 instead of committing on its own.
    protected override IReadOnlyList<string> DoomThenWriteBatches(string insertAfter)
    {
        return [$"SET XACT_ABORT ON; BEGIN TRY SELECT 1 / 0; END TRY BEGIN CATCH END CATCH; {insertAfter};"];
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

    [Fact]
    public override Task should_retry_engine_chosen_deadlock_victims_and_apply_each_call_once()
    {
        return base.should_retry_engine_chosen_deadlock_victims_and_apply_each_call_once();
    }

    [Fact]
    public override Task should_surface_the_deadlock_after_the_attempt_cap_and_apply_nothing()
    {
        return base.should_surface_the_deadlock_after_the_attempt_cap_and_apply_nothing();
    }
}
