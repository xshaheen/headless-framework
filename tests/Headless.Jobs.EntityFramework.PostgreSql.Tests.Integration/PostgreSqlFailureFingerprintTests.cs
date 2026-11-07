// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

/// <summary>Runs the failure-fingerprint conformance suite against PostgreSql.</summary>
[Collection<PostgreSqlJobsCoordinationFixture>]
public sealed class PostgreSqlFailureFingerprintTests(PostgreSqlJobsCoordinationFixture fixture)
    : JobsFailureFingerprintConformanceTests<PostgreSqlJobsCoordinationFixture>(fixture)
{
    [Fact]
    public override Task failure_and_retry_writes_store_the_carried_fingerprint()
    {
        return base.failure_and_retry_writes_store_the_carried_fingerprint();
    }

    [Fact]
    public override Task lease_lapsed_failure_stores_the_lease_lapsed_fingerprint()
    {
        return base.lease_lapsed_failure_stores_the_lease_lapsed_fingerprint();
    }

    [Fact]
    public override Task dead_node_failure_stores_the_node_not_alive_fingerprint()
    {
        return base.dead_node_failure_stores_the_node_not_alive_fingerprint();
    }

    [Fact]
    public override Task interrupted_failure_stores_the_shutdown_fingerprint()
    {
        return base.interrupted_failure_stores_the_shutdown_fingerprint();
    }

    [Fact]
    public override Task only_the_winning_write_stores_a_fingerprint_when_two_writers_race_to_fail_a_run()
    {
        return base.only_the_winning_write_stores_a_fingerprint_when_two_writers_race_to_fail_a_run();
    }

    [Fact]
    public override Task requeue_clears_the_fingerprint()
    {
        return base.requeue_clears_the_fingerprint();
    }
}
