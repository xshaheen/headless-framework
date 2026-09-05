// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

[Collection<PostgreSqlJobsCoordinationFixture>]
public sealed class PostgreSqlKeyedMigrationTests(PostgreSqlJobsCoordinationFixture fixture)
    : JobsKeyedMigrationConformanceTests<PostgreSqlJobsCoordinationFixture>(fixture)
{
    protected override bool IsPostgreSql => true;

    [Fact]
    public override Task upgrade_keeps_legacy_rows_wholly_unkeyed_and_preserves_payload_bytes() =>
        base.upgrade_keeps_legacy_rows_wholly_unkeyed_and_preserves_payload_bytes();

    [Fact]
    public override Task keyed_constraints_reject_partial_metadata_and_chain_membership() =>
        base.keyed_constraints_reject_partial_metadata_and_chain_membership();

    [Fact]
    public override Task uniqueness_covers_system_tenants_current_and_historical_generations() =>
        base.uniqueness_covers_system_tenants_current_and_historical_generations();

    [Fact]
    public override Task downgrade_preflight_rejects_retained_history_even_without_a_current_row() =>
        base.downgrade_preflight_rejects_retained_history_even_without_a_current_row();
}
