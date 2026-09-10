// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

/// <summary>Runs the misfire-recovery conformance suite against PostgreSQL.</summary>
[Collection<PostgreSqlJobsCoordinationFixture>]
public sealed class PostgreSqlRecoveryTests(PostgreSqlJobsCoordinationFixture fixture)
    : JobsRecoveryConformanceTests<PostgreSqlJobsCoordinationFixture>(fixture)
{
    [Fact]
    public override Task coalesce_materializes_one_run_stamped_with_the_earliest_missed_instant()
    {
        return base.coalesce_materializes_one_run_stamped_with_the_earliest_missed_instant();
    }

    [Fact]
    public override Task skip_materializes_nothing_and_still_advances_the_watermark()
    {
        return base.skip_materializes_nothing_and_still_advances_the_watermark();
    }

    [Fact]
    public override Task coalesce_repurposes_a_queued_occurrence_and_revokes_ownership()
    {
        return base.coalesce_repurposes_a_queued_occurrence_and_revokes_ownership();
    }

    [Fact]
    public override Task recovery_leaves_an_executing_occurrence_untouched()
    {
        return base.recovery_leaves_an_executing_occurrence_untouched();
    }

    [Fact]
    public override Task recovery_does_not_re_execute_a_completed_occurrence()
    {
        return base.recovery_does_not_re_execute_a_completed_occurrence();
    }

    [Fact]
    public override Task direct_claim_preserves_the_recovery_stamp()
    {
        return base.direct_claim_preserves_the_recovery_stamp();
    }

    [Fact]
    public override Task fallback_claim_preserves_the_recovery_stamp()
    {
        return base.fallback_claim_preserves_the_recovery_stamp();
    }

    [Fact]
    public override Task concurrent_recovery_of_one_backlog_produces_exactly_one_winner()
    {
        return base.concurrent_recovery_of_one_backlog_produces_exactly_one_winner();
    }

    [Fact]
    public override Task coalesce_steps_past_an_occupied_earliest_instant_to_the_next_missed_instant()
    {
        return base.coalesce_steps_past_an_occupied_earliest_instant_to_the_next_missed_instant();
    }

    [Fact]
    public override Task saturated_coalesce_preserves_an_unexamined_idle_n_plus_one_for_the_next_pass()
    {
        return base.saturated_coalesce_preserves_an_unexamined_idle_n_plus_one_for_the_next_pass();
    }

    [Fact]
    public override Task fingerprint_keyset_progress_survives_provider_recreation()
    {
        return base.fingerprint_keyset_progress_survives_provider_recreation();
    }

    [Fact]
    public override Task fingerprint_wrap_returns_low_id_after_exactly_full_forward_page()
    {
        return base.fingerprint_wrap_returns_low_id_after_exactly_full_forward_page();
    }
}
