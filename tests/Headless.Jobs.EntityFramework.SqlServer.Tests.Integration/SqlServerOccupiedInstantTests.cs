// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

/// <summary>Runs the occupied-instant accounting matrix against SQL Server, on both claim strategies.</summary>
[Collection<SqlServerJobsCoordinationFixture>]
public sealed class SqlServerOccupiedInstantTests(SqlServerJobsCoordinationFixture fixture)
    : JobsOccupiedInstantConformanceTests<SqlServerJobsCoordinationFixture>(fixture)
{
    [Fact]
    public override Task the_occupied_instant_matrix_governs_materialization()
    {
        return base.the_occupied_instant_matrix_governs_materialization();
    }

    [Fact]
    public override Task the_occupied_instant_matrix_governs_recovery()
    {
        return base.the_occupied_instant_matrix_governs_recovery();
    }

    [Fact]
    public override Task the_occupied_instant_matrix_governs_the_claim_path()
    {
        return base.the_occupied_instant_matrix_governs_the_claim_path();
    }

    [Fact]
    public override Task a_live_row_is_reported_over_an_older_terminal_row_at_the_same_instant()
    {
        return base.a_live_row_is_reported_over_an_older_terminal_row_at_the_same_instant();
    }
}
