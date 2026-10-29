// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

/// <summary>Runs the owner-scoped seed-reconcile conformance suite against SqlServer.</summary>
[Collection<SqlServerJobsCoordinationFixture>]
public sealed class SqlServerSeedReconcileTests(SqlServerJobsCoordinationFixture fixture)
    : JobsSeedReconcileConformanceTests<SqlServerJobsCoordinationFixture>(fixture)
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
}
