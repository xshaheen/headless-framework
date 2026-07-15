// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

/// <summary>Runs the database-clock ownership conformance suite against Postgres (expects <c>now()</c>).</summary>
[Collection<PostgreSqlJobsCoordinationFixture>]
public sealed class PostgreSqlDatabaseClockConformanceTests(PostgreSqlJobsCoordinationFixture fixture)
    : JobsDatabaseClockConformanceTests<PostgreSqlJobsCoordinationFixture>(fixture)
{
    [Fact]
    public override Task claim_and_acquire_lease_sql_is_owned_by_the_database_clock()
    {
        return base.claim_and_acquire_lease_sql_is_owned_by_the_database_clock();
    }

    [Fact]
    public override Task lease_renewal_sql_is_owned_by_the_database_clock()
    {
        return base.lease_renewal_sql_is_owned_by_the_database_clock();
    }

    [Fact]
    public override Task reclaim_and_release_sweep_sql_is_owned_by_the_database_clock()
    {
        return base.reclaim_and_release_sweep_sql_is_owned_by_the_database_clock();
    }
}
