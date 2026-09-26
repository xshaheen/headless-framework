// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

[Collection<PostgreSqlFencingFixture>]
public sealed class PostgreSqlLeasesConformanceTests(PostgreSqlFencingFixture fixture)
    : LeasesConformanceTests<PostgreSqlFencingFixture>(fixture)
{
    [Fact]
    public override Task should_refuse_the_older_generation_at_the_fence_after_a_takeover()
    {
        return base.should_refuse_the_older_generation_at_the_fence_after_a_takeover();
    }

    [Fact]
    public override Task should_report_the_live_holder_and_change_nothing_when_the_lease_is_held()
    {
        return base.should_report_the_live_holder_and_change_nothing_when_the_lease_is_held();
    }

    [Fact]
    public override Task should_take_over_an_expired_lease_and_leave_nothing_for_the_sweep()
    {
        return base.should_take_over_an_expired_lease_and_leave_nothing_for_the_sweep();
    }

    [Fact]
    public override Task should_decide_expiry_by_the_database_clock_whatever_the_application_clock()
    {
        return base.should_decide_expiry_by_the_database_clock_whatever_the_application_clock();
    }

    [Fact]
    public override Task should_grant_exactly_one_holder_and_unique_generations_when_grants_race()
    {
        return base.should_grant_exactly_one_holder_and_unique_generations_when_grants_race();
    }

    [Fact]
    public override Task should_keep_leases_of_different_tenants_and_the_host_scope_independent()
    {
        return base.should_keep_leases_of_different_tenants_and_the_host_scope_independent();
    }

    [Fact]
    public override Task should_report_stale_or_the_ended_state_when_renewing()
    {
        return base.should_report_stale_or_the_ended_state_when_renewing();
    }

    [Fact]
    public override Task should_settle_once_and_refuse_a_stale_or_expired_settlement()
    {
        return base.should_settle_once_and_refuse_a_stale_or_expired_settlement();
    }

    [Fact]
    public override Task should_release_once_and_let_the_next_grant_proceed_at_once()
    {
        return base.should_release_once_and_let_the_next_grant_proceed_at_once();
    }

    [Fact]
    public override Task should_refuse_the_fence_once_the_ttl_elapses_inside_an_open_transaction()
    {
        return base.should_refuse_the_fence_once_the_ttl_elapses_inside_an_open_transaction();
    }

    [Fact]
    public override Task should_make_a_grant_wait_for_an_open_fence_and_then_see_its_settlement()
    {
        return base.should_make_a_grant_wait_for_an_open_fence_and_then_see_its_settlement();
    }

    [Fact]
    public override Task should_issue_a_higher_generation_to_a_grant_that_waited_on_an_open_enlisted_grant()
    {
        return base.should_issue_a_higher_generation_to_a_grant_that_waited_on_an_open_enlisted_grant();
    }

    [Fact]
    public override Task should_leave_no_row_when_an_enlisted_grant_rolls_back()
    {
        return base.should_leave_no_row_when_an_enlisted_grant_rolls_back();
    }

    [Fact]
    public override Task should_mark_only_an_observed_unit_non_retryable_and_only_for_writes()
    {
        return base.should_mark_only_an_observed_unit_non_retryable_and_only_for_writes();
    }

    [Fact]
    public override Task should_refuse_a_unit_on_another_database_before_any_statement()
    {
        return base.should_refuse_a_unit_on_another_database_before_any_statement();
    }

    [Fact]
    public override Task should_hand_each_expired_lease_to_exactly_one_committed_handler_when_sweepers_race()
    {
        return base.should_hand_each_expired_lease_to_exactly_one_committed_handler_when_sweepers_race();
    }

    [Fact]
    public override Task should_roll_back_only_the_lease_whose_handler_threw_and_offer_it_again_later()
    {
        return base.should_roll_back_only_the_lease_whose_handler_threw_and_offer_it_again_later();
    }

    [Fact]
    public override Task should_not_reclaim_an_always_throwing_lease_within_one_sweep_call()
    {
        return base.should_not_reclaim_an_always_throwing_lease_within_one_sweep_call();
    }

    [Fact]
    public override Task should_sweep_only_the_requested_kind()
    {
        return base.should_sweep_only_the_requested_kind();
    }

    [Fact]
    public override Task should_purge_only_old_ended_leases_and_never_reissue_a_generation()
    {
        return base.should_purge_only_old_ended_leases_and_never_reissue_a_generation();
    }

    [Fact]
    public override Task should_purge_nothing_without_failing_when_the_age_exceeds_the_timestamp_range()
    {
        return base.should_purge_nothing_without_failing_when_the_age_exceeds_the_timestamp_range();
    }
}
