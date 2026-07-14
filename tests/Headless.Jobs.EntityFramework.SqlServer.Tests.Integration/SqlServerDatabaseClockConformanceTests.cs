// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

/// <summary>Runs the database-clock ownership conformance suite against SQL Server (expects <c>GETUTCDATE()</c>).</summary>
[Collection<SqlServerJobsCoordinationFixture>]
public sealed class SqlServerDatabaseClockConformanceTests(SqlServerJobsCoordinationFixture fixture)
    : JobsDatabaseClockConformanceTests<SqlServerJobsCoordinationFixture>(fixture)
{
    [Fact]
    public override Task claim_and_acquire_lease_sql_is_owned_by_the_database_clock() =>
        base.claim_and_acquire_lease_sql_is_owned_by_the_database_clock();

    [Fact]
    public override Task lease_renewal_sql_is_owned_by_the_database_clock() =>
        base.lease_renewal_sql_is_owned_by_the_database_clock();

    [Fact]
    public override Task reclaim_and_release_sweep_sql_is_owned_by_the_database_clock() =>
        base.reclaim_and_release_sweep_sql_is_owned_by_the_database_clock();
}
