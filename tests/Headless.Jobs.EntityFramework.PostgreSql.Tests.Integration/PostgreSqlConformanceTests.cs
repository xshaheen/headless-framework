// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

/// <summary>Runs the cross-provider Jobs+Coordination conformance suite against Postgres.</summary>
[Collection<PostgreSqlJobsCoordinationFixture>]
public sealed class PostgreSqlConformanceTests(PostgreSqlJobsCoordinationFixture fixture)
    : JobsCoordinationConformanceTests<PostgreSqlJobsCoordinationFixture>(fixture)
{
    [Fact]
    public override Task queued_job_is_stamped_with_the_node_incarnation_owner() =>
        base.queued_job_is_stamped_with_the_node_incarnation_owner();

    [Fact]
    public override Task reclaim_touches_only_the_dead_incarnations_non_terminal_rows() =>
        base.reclaim_touches_only_the_dead_incarnations_non_terminal_rows();

    [Fact]
    public override Task reclaim_is_idempotent_a_second_pass_affects_zero_rows() =>
        base.reclaim_is_idempotent_a_second_pass_affects_zero_rows();

    [Fact]
    public override Task surviving_node_recovers_a_crashed_nodes_work_via_node_left_event() =>
        base.surviving_node_recovers_a_crashed_nodes_work_via_node_left_event();

    [Fact]
    public override Task dead_node_with_mark_failed_policy_transitions_in_flight_row_to_failed() =>
        base.dead_node_with_mark_failed_policy_transitions_in_flight_row_to_failed();

    [Fact]
    public override Task dead_node_with_skip_policy_transitions_in_flight_row_to_skipped() =>
        base.dead_node_with_skip_policy_transitions_in_flight_row_to_skipped();

    [Fact]
    public override Task completion_is_fenced_on_ownership_and_non_terminal_status() =>
        base.completion_is_fenced_on_ownership_and_non_terminal_status();

    [Fact]
    public override Task cron_occurrence_is_stamped_with_the_node_death_policy() =>
        base.cron_occurrence_is_stamped_with_the_node_death_policy();

    [Fact]
    public override Task running_job_renews_its_own_lease_but_a_lost_lease_renews_zero_rows() =>
        base.running_job_renews_its_own_lease_but_a_lost_lease_renews_zero_rows();

    [Fact]
    public override Task renewal_returns_the_membership_sentinel_when_membership_is_not_established() =>
        base.renewal_returns_the_membership_sentinel_when_membership_is_not_established();

    [Fact]
    public override Task stalled_lapsed_lease_inprogress_rows_are_reclaimed_per_policy() =>
        base.stalled_lapsed_lease_inprogress_rows_are_reclaimed_per_policy();

    [Fact]
    public override Task stalled_reclaim_uses_the_db_clock_not_a_skewed_reclaimer_clock() =>
        base.stalled_reclaim_uses_the_db_clock_not_a_skewed_reclaimer_clock();

    [Fact]
    public override Task cron_stalled_reclaim_uses_the_db_clock_and_terminalizes_per_policy() =>
        base.cron_stalled_reclaim_uses_the_db_clock_and_terminalizes_per_policy();

    [Fact]
    public override Task cron_running_occurrence_renews_but_queued_or_foreign_renews_zero() =>
        base.cron_running_occurrence_renews_but_queued_or_foreign_renews_zero();

    [Fact]
    public override Task cron_completion_is_fenced_on_ownership_and_non_terminal_status() =>
        base.cron_completion_is_fenced_on_ownership_and_non_terminal_status();

    [Fact]
    public override Task node_death_sweep_leaves_a_valid_lease_inprogress_row_to_the_lease() =>
        base.node_death_sweep_leaves_a_valid_lease_inprogress_row_to_the_lease();
}
