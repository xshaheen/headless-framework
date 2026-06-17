// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

/// <summary>Runs the cross-provider Jobs+Coordination conformance suite against SQL Server.</summary>
[Collection<SqlServerJobsCoordinationFixture>]
public sealed class SqlServerConformanceTests(SqlServerJobsCoordinationFixture fixture)
    : JobsCoordinationConformanceTests<SqlServerJobsCoordinationFixture>(fixture)
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
}
