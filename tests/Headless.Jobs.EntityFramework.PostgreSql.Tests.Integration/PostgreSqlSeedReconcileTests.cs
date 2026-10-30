// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

/// <summary>Runs the owner-scoped seed-reconcile conformance suite against PostgreSql.</summary>
[Collection<PostgreSqlJobsCoordinationFixture>]
public sealed class PostgreSqlSeedReconcileTests(PostgreSqlJobsCoordinationFixture fixture)
    : JobsSeedReconcileConformanceTests<PostgreSqlJobsCoordinationFixture>(fixture)
{
    [Fact]
    public override Task removing_a_declaration_with_a_running_occurrence_retires_it_and_deletes_pending_occurrences()
    {
        return base.removing_a_declaration_with_a_running_occurrence_retires_it_and_deletes_pending_occurrences();
    }

    [Fact]
    public override Task the_next_seed_deletes_a_retired_declaration_after_its_running_occurrence_completes()
    {
        return base.the_next_seed_deletes_a_retired_declaration_after_its_running_occurrence_completes();
    }

    [Fact]
    public override Task redeclaring_a_retired_row_restores_and_unpauses_it_even_when_its_schedule_has_not_changed()
    {
        return base.redeclaring_a_retired_row_restores_and_unpauses_it_even_when_its_schedule_has_not_changed();
    }

    [Fact]
    public override Task seed_leaves_a_runtime_row_of_an_attributed_function_unchanged()
    {
        return base.seed_leaves_a_runtime_row_of_an_attributed_function_unchanged();
    }

    [Fact]
    public override Task removing_a_function_deletes_only_this_owners_seeded_row()
    {
        return base.removing_a_function_deletes_only_this_owners_seeded_row();
    }

    [Fact]
    public override Task seed_keeps_the_rows_of_an_owner_that_differs_only_by_case()
    {
        return base.seed_keeps_the_rows_of_an_owner_that_differs_only_by_case();
    }

    [Fact]
    public override Task seed_restores_a_declarations_zone_an_operator_changed()
    {
        return base.seed_restores_a_declarations_zone_an_operator_changed();
    }

    [Fact]
    public override Task seed_restores_the_declared_job_and_arguments_of_a_repointed_row()
    {
        return base.seed_restores_the_declared_job_and_arguments_of_a_repointed_row();
    }

    [Fact]
    public override Task concurrent_seeds_restore_a_drifted_declaration_once_and_delete_a_removed_one()
    {
        return base.concurrent_seeds_restore_a_drifted_declaration_once_and_delete_a_removed_one();
    }

    [Fact]
    public override Task concurrent_seeds_of_one_set_leave_one_row_per_function()
    {
        return base.concurrent_seeds_of_one_set_leave_one_row_per_function();
    }

    [Fact]
    public override Task seed_restores_the_expression_of_a_paused_row_and_keeps_it_paused()
    {
        return base.seed_restores_the_expression_of_a_paused_row_and_keeps_it_paused();
    }

    [Fact]
    public override Task declarations_of_one_function_seed_one_row_each_with_their_declared_values()
    {
        return base.declarations_of_one_function_seed_one_row_each_with_their_declared_values();
    }

    [Fact]
    public override Task removing_a_declaration_deletes_its_row_and_occurrences_and_nothing_else()
    {
        return base.removing_a_declaration_deletes_its_row_and_occurrences_and_nothing_else();
    }

    [Fact]
    public override Task an_attribute_cron_and_a_declaration_of_one_function_keep_both_rows()
    {
        return base.an_attribute_cron_and_a_declaration_of_one_function_keep_both_rows();
    }

    [Fact]
    public override Task renaming_a_declaration_key_replaces_its_row()
    {
        return base.renaming_a_declaration_key_replaces_its_row();
    }

    [Fact]
    public override Task seed_restores_a_declarations_expression_and_keeps_the_operators_retries_and_pause()
    {
        return base.seed_restores_a_declarations_expression_and_keeps_the_operators_retries_and_pause();
    }

    [Fact]
    public override Task changed_declared_arguments_rewrite_the_payload_and_version_once()
    {
        return base.changed_declared_arguments_rewrite_the_payload_and_version_once();
    }

    [Fact]
    public override Task the_same_arguments_written_compressed_and_reordered_are_not_rewritten()
    {
        return base.the_same_arguments_written_compressed_and_reordered_are_not_rewritten();
    }

    [Fact]
    public override Task a_changed_contract_version_with_unchanged_arguments_is_rewritten_once()
    {
        return base.a_changed_contract_version_with_unchanged_arguments_is_rewritten_once();
    }

    [Fact]
    public override Task a_stored_payload_that_is_not_json_is_overwritten_without_failing_the_seed()
    {
        return base.a_stored_payload_that_is_not_json_is_overwritten_without_failing_the_seed();
    }

    [Fact]
    public override Task an_attribute_cron_rows_payload_and_version_are_never_rewritten()
    {
        return base.an_attribute_cron_rows_payload_and_version_are_never_rewritten();
    }
}
