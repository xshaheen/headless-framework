// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Microsoft.Data.Sqlite;

namespace Tests;

/// <summary>The fenced-row conformance suite on SQLite.</summary>
[Collection<SqliteFencingFixture>]
public sealed class SqliteFencedRowsConformanceTests(SqliteFencingFixture fixture)
    : FencedRowsConformanceTests<SqliteFencingFixture>(fixture)
{
    // SQLite's default begin takes the write lock, waiting under the connection's timeout instead of the command's; a
    // deferred begin leaves that wait to the first write.
    protected override Task<DbTransaction> BeginWaitingTransactionAsync(DbConnection connection) =>
        Task.FromResult<DbTransaction>(((SqliteConnection)connection).BeginTransaction(deferred: true));

    [Fact]
    public override Task should_grant_generation_one_to_a_fresh_row_and_store_the_lease_on_it()
    {
        return base.should_grant_generation_one_to_a_fresh_row_and_store_the_lease_on_it();
    }

    [Fact]
    public override Task should_return_null_and_insert_nothing_when_no_row_has_the_key()
    {
        return base.should_return_null_and_insert_nothing_when_no_row_has_the_key();
    }

    [Fact]
    public override Task should_hand_back_the_live_attempt_unchanged_when_the_same_holder_grants_again()
    {
        return base.should_hand_back_the_live_attempt_unchanged_when_the_same_holder_grants_again();
    }

    [Fact]
    public override Task should_report_the_live_holder_and_change_nothing_when_the_row_is_held()
    {
        return base.should_report_the_live_holder_and_change_nothing_when_the_row_is_held();
    }

    [Fact]
    public override Task should_take_over_an_expired_attempt_count_it_and_refuse_the_older_generation()
    {
        return base.should_take_over_an_expired_attempt_count_it_and_refuse_the_older_generation();
    }

    [Fact]
    public override Task should_report_an_expired_attempt_and_change_nothing_when_the_grant_waits_for_the_sweep()
    {
        return base.should_report_an_expired_attempt_and_change_nothing_when_the_grant_waits_for_the_sweep();
    }

    [Fact]
    public override Task should_claim_only_grantable_rows_each_under_its_own_next_generation()
    {
        return base.should_claim_only_grantable_rows_each_under_its_own_next_generation();
    }

    [Fact]
    public override Task should_claim_only_what_an_open_claim_left_once_it_commits()
    {
        return base.should_claim_only_what_an_open_claim_left_once_it_commits();
    }

    [Fact]
    public override Task should_renew_a_live_generation_and_report_why_any_other_is_refused()
    {
        return base.should_renew_a_live_generation_and_report_why_any_other_is_refused();
    }

    [Fact]
    public override Task should_settle_once_and_report_the_settlement_to_a_retry_or_a_release()
    {
        return base.should_settle_once_and_report_the_settlement_to_a_retry_or_a_release();
    }

    [Fact]
    public override Task should_release_once_and_let_the_next_grant_proceed_at_once()
    {
        return base.should_release_once_and_let_the_next_grant_proceed_at_once();
    }

    [Fact]
    public override Task should_refuse_a_settlement_of_an_expired_or_replaced_generation()
    {
        return base.should_refuse_a_settlement_of_an_expired_or_replaced_generation();
    }

    [Fact]
    public override Task should_revoke_the_active_attempt_live_or_expired_and_report_revoked_to_its_verbs()
    {
        return base.should_revoke_the_active_attempt_live_or_expired_and_report_revoked_to_its_verbs();
    }

    [Fact]
    public override Task should_release_tokens_of_different_generations_in_one_call_each_judged_on_its_own()
    {
        return base.should_release_tokens_of_different_generations_in_one_call_each_judged_on_its_own();
    }

    [Fact]
    public override Task should_apply_a_composed_settle_and_refuse_it_after_a_takeover()
    {
        return base.should_apply_a_composed_settle_and_refuse_it_after_a_takeover();
    }

    [Fact]
    public override Task should_write_while_current_and_tell_a_refused_condition_from_a_lost_lease()
    {
        return base.should_write_while_current_and_tell_a_refused_condition_from_a_lost_lease();
    }

    [Fact]
    public override Task should_refuse_a_composed_write_whose_row_lock_was_held_past_the_expiry()
    {
        return base.should_refuse_a_composed_write_whose_row_lock_was_held_past_the_expiry();
    }

    [Fact]
    public override Task should_give_up_waiting_on_a_held_row_after_the_tables_command_timeout()
    {
        return base.should_give_up_waiting_on_a_held_row_after_the_tables_command_timeout();
    }

    [Fact]
    public override Task should_grant_a_subtree_the_roots_deadline_with_a_generation_per_child()
    {
        return base.should_grant_a_subtree_the_roots_deadline_with_a_generation_per_child();
    }

    [Fact]
    public override Task should_fence_inside_the_callers_transaction_and_judge_every_other_generation()
    {
        return base.should_fence_inside_the_callers_transaction_and_judge_every_other_generation();
    }

    [Fact]
    public override Task should_read_each_status_by_the_database_clock_without_a_transaction_or_a_write()
    {
        return base.should_read_each_status_by_the_database_clock_without_a_transaction_or_a_write();
    }

    [Fact]
    public override Task should_abandon_only_expired_active_attempts_and_count_each_as_a_takeover()
    {
        return base.should_abandon_only_expired_active_attempts_and_count_each_as_a_takeover();
    }

    [Fact]
    public override Task should_land_the_consumers_write_only_on_rows_a_revocation_or_a_sweep_ends()
    {
        return base.should_land_the_consumers_write_only_on_rows_a_revocation_or_a_sweep_ends();
    }

    [Fact]
    public override Task should_throw_for_a_token_another_table_issued_before_any_statement()
    {
        return base.should_throw_for_a_token_another_table_issued_before_any_statement();
    }

    [Fact]
    public override Task should_throw_when_a_write_binds_a_reserved_parameter_or_a_settle_takes_a_condition()
    {
        return base.should_throw_when_a_write_binds_a_reserved_parameter_or_a_settle_takes_a_condition();
    }

    [Fact]
    public override Task should_draw_store_wide_increasing_generations_from_the_sequence()
    {
        return base.should_draw_store_wide_increasing_generations_from_the_sequence();
    }

    [Fact]
    public override Task should_grant_and_settle_on_a_table_and_a_sequence_named_without_a_schema()
    {
        return base.should_grant_and_settle_on_a_table_and_a_sequence_named_without_a_schema();
    }

    [Fact]
    public override Task should_land_the_consumers_write_on_each_row_a_batch_release_ends()
    {
        return base.should_land_the_consumers_write_on_each_row_a_batch_release_ends();
    }

    [Fact]
    public override Task should_renew_the_lease_with_a_write_that_applies_and_leave_it_when_the_write_is_refused()
    {
        return base.should_renew_the_lease_with_a_write_that_applies_and_leave_it_when_the_write_is_refused();
    }

    [Fact]
    public override Task should_end_under_a_condition_read_columns_back_and_tell_a_refused_condition_from_a_lost_lease()
    {
        return base.should_end_under_a_condition_read_columns_back_and_tell_a_refused_condition_from_a_lost_lease();
    }

    [Fact]
    public override Task should_claim_through_a_scope_only_rows_the_lease_lets_a_new_attempt_take()
    {
        return base.should_claim_through_a_scope_only_rows_the_lease_lets_a_new_attempt_take();
    }

    [Fact]
    public override Task should_revoke_only_active_attempts_the_listed_holders_hold_and_land_the_write()
    {
        return base.should_revoke_only_active_attempts_the_listed_holders_hold_and_land_the_write();
    }

    [Fact]
    public override Task should_hold_a_holder_to_the_limit_its_table_was_created_with()
    {
        return base.should_hold_a_holder_to_the_limit_its_table_was_created_with();
    }
}
