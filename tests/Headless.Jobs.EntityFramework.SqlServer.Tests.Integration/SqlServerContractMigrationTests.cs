// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

[Collection<SqlServerJobsCoordinationFixture>]
public sealed class SqlServerContractMigrationTests(SqlServerJobsCoordinationFixture fixture)
    : JobsContractMigrationConformanceTests<SqlServerJobsCoordinationFixture>(fixture)
{
    protected override bool IsPostgreSql => false;

    [Fact]
    public override Task legacy_upgrade_preserves_available_payload_bytes_and_ordinal_contract_identity() =>
        base.legacy_upgrade_preserves_available_payload_bytes_and_ordinal_contract_identity();

    [Fact]
    public override Task invalid_legacy_identity_aborts_before_any_schema_or_data_change() =>
        base.invalid_legacy_identity_aborts_before_any_schema_or_data_change();

    [Fact]
    public override Task supplementary_boundary_name_survives_without_truncation() =>
        base.supplementary_boundary_name_survives_without_truncation();

    [Fact]
    public override Task upgraded_schema_requires_explicit_nonblank_version_without_database_default() =>
        base.upgraded_schema_requires_explicit_nonblank_version_without_database_default();

    [Fact]
    public override Task downgrade_preflight_refuses_nonlegacy_contract_in_every_executable_table() =>
        base.downgrade_preflight_refuses_nonlegacy_contract_in_every_executable_table();
}
