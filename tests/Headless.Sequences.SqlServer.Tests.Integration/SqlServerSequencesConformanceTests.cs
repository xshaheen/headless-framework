// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

[Collection<SqlServerSequencesFixture>]
public sealed class SqlServerSequencesConformanceTests(SqlServerSequencesFixture fixture)
    : SequencesConformanceTests<SqlServerSequencesFixture>(fixture)
{
    [Fact]
    public override Task should_issue_exactly_one_to_hundred_when_two_hosts_call_concurrently()
    {
        return base.should_issue_exactly_one_to_hundred_when_two_hosts_call_concurrently();
    }

    [Fact]
    public override Task should_create_the_counter_once_when_first_calls_race_on_a_new_key()
    {
        return base.should_create_the_counter_once_when_first_calls_race_on_a_new_key();
    }

    [Fact]
    public override Task should_keep_counters_of_different_tenants_and_the_host_scope_independent()
    {
        return base.should_keep_counters_of_different_tenants_and_the_host_scope_independent();
    }

    [Fact]
    public override Task should_keep_partitions_of_one_name_independent()
    {
        return base.should_keep_partitions_of_one_name_independent();
    }

    [Fact]
    public override Task should_treat_a_null_and_an_empty_partition_as_the_same_counter()
    {
        return base.should_treat_a_null_and_an_empty_partition_as_the_same_counter();
    }

    [Fact]
    public override Task should_treat_names_differing_only_by_case_as_different_counters()
    {
        return base.should_treat_names_differing_only_by_case_as_different_counters();
    }

    [Fact]
    public override Task should_apply_the_registered_start_and_step()
    {
        return base.should_apply_the_registered_start_and_step();
    }

    [Fact]
    public override Task should_start_a_reservation_on_a_new_key_at_the_policy_start()
    {
        return base.should_start_a_reservation_on_a_new_key_at_the_policy_start();
    }

    [Fact]
    public override Task should_reserve_a_consecutive_range_and_continue_after_it()
    {
        return base.should_reserve_a_consecutive_range_and_continue_after_it();
    }

    [Fact]
    public override Task should_never_overlap_concurrent_reservations()
    {
        return base.should_never_overlap_concurrent_reservations();
    }

    [Fact]
    public override Task should_throw_and_leave_the_counter_unchanged_when_cancelled_before_the_call()
    {
        return base.should_throw_and_leave_the_counter_unchanged_when_cancelled_before_the_call();
    }

    [Fact]
    public override Task should_advance_at_most_one_step_per_call_cancelled_in_flight()
    {
        return base.should_advance_at_most_one_step_per_call_cancelled_in_flight();
    }

    [Fact]
    public override Task should_reject_invalid_key_parts_before_any_statement()
    {
        return base.should_reject_invalid_key_parts_before_any_statement();
    }

    [Fact]
    public override Task should_accept_key_parts_at_their_maximum_lengths()
    {
        return base.should_accept_key_parts_at_their_maximum_lengths();
    }

    [Fact]
    public override Task should_give_the_next_unit_the_number_a_rolled_back_unit_took()
    {
        return base.should_give_the_next_unit_the_number_a_rolled_back_unit_took();
    }

    [Fact]
    public override Task should_block_the_second_unit_until_the_first_commits()
    {
        return base.should_block_the_second_unit_until_the_first_commits();
    }

    [Fact]
    public override Task should_hand_the_waiting_unit_the_number_of_a_unit_that_rolls_back_on_a_new_key()
    {
        return base.should_hand_the_waiting_unit_the_number_of_a_unit_that_rolls_back_on_a_new_key();
    }

    [Fact]
    public override Task should_return_consecutive_values_for_sequential_calls_in_one_unit()
    {
        return base.should_return_consecutive_values_for_sequential_calls_in_one_unit();
    }

    [Fact]
    public override Task should_key_gap_free_counters_by_the_current_tenant()
    {
        return base.should_key_gap_free_counters_by_the_current_tenant();
    }

    [Fact]
    public override Task should_keep_an_owned_unit_replayable_after_a_gap_free_call()
    {
        return base.should_keep_an_owned_unit_replayable_after_a_gap_free_call();
    }

    [Fact]
    public override Task should_mark_an_observed_unit_non_retryable_and_commit_with_its_transaction()
    {
        return base.should_mark_an_observed_unit_non_retryable_and_commit_with_its_transaction();
    }

    [Fact]
    public override Task should_refuse_a_unit_on_another_database_before_any_statement()
    {
        return base.should_refuse_a_unit_on_another_database_before_any_statement();
    }

    [Fact]
    public override Task should_refuse_a_unit_that_already_completed()
    {
        return base.should_refuse_a_unit_that_already_completed();
    }

    [Fact]
    public override Task should_refuse_a_gap_free_counter_through_the_injected_generator()
    {
        return base.should_refuse_a_gap_free_counter_through_the_injected_generator();
    }

    [Fact]
    public override Task should_refuse_a_fast_counter_through_the_unit()
    {
        return base.should_refuse_a_fast_counter_through_the_unit();
    }
}
