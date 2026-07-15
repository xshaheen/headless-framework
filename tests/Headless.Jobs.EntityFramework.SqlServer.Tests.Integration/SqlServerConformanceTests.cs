// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

/// <summary>Runs the cross-provider Jobs+Coordination conformance suite against SQL Server.</summary>
[Collection<SqlServerJobsCoordinationFixture>]
public sealed class SqlServerConformanceTests(SqlServerJobsCoordinationFixture fixture)
    : JobsCoordinationConformanceTests<SqlServerJobsCoordinationFixture>(fixture)
{
    [Fact]
    public override Task queued_job_is_stamped_with_the_node_incarnation_owner()
    {
        return base.queued_job_is_stamped_with_the_node_incarnation_owner();
    }

    [Fact]
    public override Task queued_job_lease_uses_the_db_clock_not_a_skewed_claimant_clock()
    {
        return base.queued_job_lease_uses_the_db_clock_not_a_skewed_claimant_clock();
    }

    [Fact]
    public override Task portable_claim_results_return_database_timestamps_and_batch_new_crons()
    {
        return base.portable_claim_results_return_database_timestamps_and_batch_new_crons();
    }

    [Fact]
    public override Task native_claim_eligibility_uses_the_db_clock_not_a_fast_application_clock()
    {
        return base.native_claim_eligibility_uses_the_db_clock_not_a_fast_application_clock();
    }

    [Fact]
    public override Task reclaim_touches_only_the_dead_incarnations_non_terminal_rows()
    {
        return base.reclaim_touches_only_the_dead_incarnations_non_terminal_rows();
    }

    [Fact]
    public override Task reclaim_is_idempotent_a_second_pass_affects_zero_rows()
    {
        return base.reclaim_is_idempotent_a_second_pass_affects_zero_rows();
    }

    [Fact]
    public override Task surviving_node_recovers_a_crashed_nodes_work_via_node_left_event()
    {
        return base.surviving_node_recovers_a_crashed_nodes_work_via_node_left_event();
    }

    [Fact]
    public override Task dead_node_with_mark_failed_policy_transitions_in_flight_row_to_failed()
    {
        return base.dead_node_with_mark_failed_policy_transitions_in_flight_row_to_failed();
    }

    [Fact]
    public override Task dead_node_with_skip_policy_transitions_in_flight_row_to_skipped()
    {
        return base.dead_node_with_skip_policy_transitions_in_flight_row_to_skipped();
    }

    [Fact]
    public override Task completion_is_fenced_on_ownership_and_non_terminal_status()
    {
        return base.completion_is_fenced_on_ownership_and_non_terminal_status();
    }

    [Fact]
    public override Task cron_occurrence_is_stamped_with_the_node_death_policy()
    {
        return base.cron_occurrence_is_stamped_with_the_node_death_policy();
    }

    [Fact]
    public override Task running_job_renews_its_own_lease_but_a_lost_lease_renews_zero_rows()
    {
        return base.running_job_renews_its_own_lease_but_a_lost_lease_renews_zero_rows();
    }

    [Fact]
    public override Task renewal_returns_the_membership_sentinel_when_membership_is_not_established()
    {
        return base.renewal_returns_the_membership_sentinel_when_membership_is_not_established();
    }

    [Fact]
    public override Task stalled_lapsed_lease_inprogress_rows_are_reclaimed_per_policy()
    {
        return base.stalled_lapsed_lease_inprogress_rows_are_reclaimed_per_policy();
    }

    [Fact]
    public override Task stalled_reclaim_uses_the_db_clock_not_a_skewed_reclaimer_clock()
    {
        return base.stalled_reclaim_uses_the_db_clock_not_a_skewed_reclaimer_clock();
    }

    [Fact]
    public override Task cron_stalled_reclaim_uses_the_db_clock_and_terminalizes_per_policy()
    {
        return base.cron_stalled_reclaim_uses_the_db_clock_and_terminalizes_per_policy();
    }

    [Fact]
    public override Task cron_running_occurrence_renews_but_queued_or_foreign_renews_zero()
    {
        return base.cron_running_occurrence_renews_but_queued_or_foreign_renews_zero();
    }

    [Fact]
    public override Task cron_completion_is_fenced_on_ownership_and_non_terminal_status()
    {
        return base.cron_completion_is_fenced_on_ownership_and_non_terminal_status();
    }

    [Fact]
    public override Task node_death_sweep_leaves_a_valid_lease_inprogress_row_to_the_lease()
    {
        return base.node_death_sweep_leaves_a_valid_lease_inprogress_row_to_the_lease();
    }

    [Fact]
    public override Task queueing_a_time_job_claims_its_child_tree()
    {
        return base.queueing_a_time_job_claims_its_child_tree();
    }

    [Fact]
    public override Task fallback_queueing_a_time_job_claims_its_child_tree()
    {
        return base.fallback_queueing_a_time_job_claims_its_child_tree();
    }

    [Fact]
    public override Task unified_context_inprogress_stamp_requires_a_queued_row()
    {
        return base.unified_context_inprogress_stamp_requires_a_queued_row();
    }

    [Fact]
    public override Task cron_unified_context_inprogress_stamp_requires_a_queued_row()
    {
        return base.cron_unified_context_inprogress_stamp_requires_a_queued_row();
    }
}

/// <summary>Runs native Jobs claim conformance through SQL Server production registration.</summary>
[Collection<SqlServerJobsCoordinationFixture>]
public sealed class SqlServerClaimConformanceTests(SqlServerJobsCoordinationFixture fixture)
    : JobsClaimConformanceTests<SqlServerJobsCoordinationFixture>(fixture)
{
    [Fact]
    public override Task synchronized_workers_claim_disjoint_time_job_roots_and_complete_descendant_stamps()
    {
        return base.synchronized_workers_claim_disjoint_time_job_roots_and_complete_descendant_stamps();
    }

    [Fact]
    public override Task synchronized_workers_claim_disjoint_fallback_cron_occurrences()
    {
        return base.synchronized_workers_claim_disjoint_fallback_cron_occurrences();
    }

    [Fact]
    public override Task expired_existing_cron_claim_requires_retry_policy()
    {
        return base.expired_existing_cron_claim_requires_retry_policy();
    }

    [Fact]
    public override Task direct_cron_claim_applies_the_full_acquire_predicate_matrix()
    {
        return base.direct_cron_claim_applies_the_full_acquire_predicate_matrix();
    }

    [Fact]
    public override Task expired_fallback_cron_claim_requires_retry_policy()
    {
        return base.expired_fallback_cron_claim_requires_retry_policy();
    }

    [Fact]
    public override Task many_synchronized_workers_claim_each_fallback_cron_occurrence_once()
    {
        return base.many_synchronized_workers_claim_each_fallback_cron_occurrence_once();
    }

    [Fact]
    public override Task incompatible_native_model_falls_back_to_ef_cas_through_production_registration()
    {
        return base.incompatible_native_model_falls_back_to_ef_cas_through_production_registration();
    }

    [Fact]
    public override Task concurrent_missing_cron_occurrence_creation_is_deduplicated()
    {
        return base.concurrent_missing_cron_occurrence_creation_is_deduplicated();
    }

    [Fact]
    public override Task long_cron_claim_transaction_publishes_a_fresh_lease()
    {
        return base.long_cron_claim_transaction_publishes_a_fresh_lease();
    }
}
