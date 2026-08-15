// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

/// <summary>Runs the cron schedule-position advance conformance suite against SQL Server.</summary>
[Collection<SqlServerJobsCoordinationFixture>]
public sealed class SqlServerSchedulePositionTests(SqlServerJobsCoordinationFixture fixture)
    : JobsSchedulePositionConformanceTests<SqlServerJobsCoordinationFixture>(fixture)
{
    [Fact]
    public override Task advance_from_the_observed_watermark_persists_the_new_position()
    {
        return base.advance_from_the_observed_watermark_persists_the_new_position();
    }

    [Fact]
    public override Task advance_with_a_stale_watermark_changes_nothing()
    {
        return base.advance_with_a_stale_watermark_changes_nothing();
    }

    [Fact]
    public override Task advance_with_a_stale_schedule_revision_changes_nothing()
    {
        return base.advance_with_a_stale_schedule_revision_changes_nothing();
    }

    [Fact]
    public override Task advance_against_a_paused_definition_changes_nothing()
    {
        return base.advance_against_a_paused_definition_changes_nothing();
    }

    [Fact]
    public override Task concurrent_advances_from_the_same_watermark_produce_exactly_one_winner()
    {
        return base.concurrent_advances_from_the_same_watermark_produce_exactly_one_winner();
    }

    [Fact]
    public override Task advancing_one_definition_leaves_a_sibling_untouched()
    {
        return base.advancing_one_definition_leaves_a_sibling_untouched();
    }

    [Fact]
    public override Task due_ness_and_the_returned_instant_follow_the_database_clock_not_a_skewed_node_clock()
    {
        return base.due_ness_and_the_returned_instant_follow_the_database_clock_not_a_skewed_node_clock();
    }

    [Fact]
    public override Task queueing_an_instant_with_a_terminal_occurrence_materializes_nothing()
    {
        return base.queueing_an_instant_with_a_terminal_occurrence_materializes_nothing();
    }

    [Fact]
    public override Task migrate_resets_the_position_when_the_code_defined_expression_changes()
    {
        return base.migrate_resets_the_position_when_the_code_defined_expression_changes();
    }

    [Fact]
    public override Task dispatch_selection_excludes_definitions_with_durable_fingerprint_defer_state()
    {
        return base.dispatch_selection_excludes_definitions_with_durable_fingerprint_defer_state();
    }

    [Fact]
    public override Task dispatch_selection_resumes_past_a_full_page_of_excluded_definitions()
    {
        return base.dispatch_selection_resumes_past_a_full_page_of_excluded_definitions();
    }

    [Fact]
    public override Task materialization_survives_restart_and_the_idle_occurrence_is_claimed_later() =>
        base.materialization_survives_restart_and_the_idle_occurrence_is_claimed_later();

    [Fact]
    public override Task concurrent_materializations_commit_one_position_and_one_occurrence() =>
        base.concurrent_materializations_commit_one_position_and_one_occurrence();

    [Fact]
    public override Task terminal_occurrence_is_an_explicit_committed_outcome_without_rematerialization() =>
        base.terminal_occurrence_is_an_explicit_committed_outcome_without_rematerialization();

    [Fact]
    public override Task existing_non_terminal_occurrence_is_reused_and_position_advances() =>
        base.existing_non_terminal_occurrence_is_reused_and_position_advances();

    [Fact]
    public override Task failure_after_the_position_update_rolls_back_position_and_occurrence() =>
        base.failure_after_the_position_update_rolls_back_position_and_occurrence();

    [Fact]
    public override Task stale_and_future_materializations_are_distinct_no_mutation_outcomes() =>
        base.stale_and_future_materializations_are_distinct_no_mutation_outcomes();

    [Fact]
    public override Task cancellation_before_materialization_changes_neither_position_nor_occurrences() =>
        base.cancellation_before_materialization_changes_neither_position_nor_occurrences();

    [Fact]
    public override Task creating_a_definition_seeds_its_position_from_the_store_clock() =>
        base.creating_a_definition_seeds_its_position_from_the_store_clock();

    [Fact]
    public override Task creating_a_batch_of_definitions_seeds_every_position_from_the_store_clock() =>
        base.creating_a_batch_of_definitions_seeds_every_position_from_the_store_clock();

    [Fact]
    public override Task a_coordinated_creation_seeds_its_position_from_the_store_clock() =>
        base.a_coordinated_creation_seeds_its_position_from_the_store_clock();

    [Fact]
    public override Task a_coordinated_creation_is_anchored_at_insertion_not_at_transaction_start() =>
        base.a_coordinated_creation_is_anchored_at_insertion_not_at_transaction_start();

    [Fact]
    public override Task a_tick_between_creation_and_the_first_poll_is_recovered_not_skipped() =>
        base.a_tick_between_creation_and_the_first_poll_is_recovered_not_skipped();
}
