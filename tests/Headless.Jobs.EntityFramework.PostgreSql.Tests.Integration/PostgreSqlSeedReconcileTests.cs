// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

/// <summary>Runs the owner-scoped seed-reconcile conformance suite against PostgreSql.</summary>
[Collection<PostgreSqlJobsCoordinationFixture>]
public sealed class PostgreSqlSeedReconcileTests(PostgreSqlJobsCoordinationFixture fixture)
    : JobsSeedReconcileConformanceTests<PostgreSqlJobsCoordinationFixture>(fixture)
{
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
}
