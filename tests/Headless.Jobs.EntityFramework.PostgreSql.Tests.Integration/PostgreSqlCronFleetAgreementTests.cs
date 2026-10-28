// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

/// <summary>Runs the two-node cron fleet agreement scenarios against PostgreSQL.</summary>
[Collection<PostgreSqlJobsCoordinationFixture>]
public sealed class PostgreSqlCronFleetAgreementTests(PostgreSqlJobsCoordinationFixture fixture)
    : JobsCronFleetAgreementConformanceTests<PostgreSqlJobsCoordinationFixture>(fixture)
{
    [Fact]
    public override Task a_stale_definition_stays_unrebased_while_a_sweeping_peer_publishes_other_rules()
    {
        return base.a_stale_definition_stays_unrebased_while_a_sweeping_peer_publishes_other_rules();
    }

    [Fact]
    public override Task a_stale_definition_is_rebased_once_when_every_sweeping_node_publishes_the_same_rules()
    {
        return base.a_stale_definition_is_rebased_once_when_every_sweeping_node_publishes_the_same_rules();
    }
}
