// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

[Collection<PostgreSqlJobsCoordinationFixture>]
public sealed class PostgreSqlKeyedSchedulingTests(PostgreSqlJobsCoordinationFixture fixture)
    : JobsKeyedSchedulingConformanceTests<PostgreSqlJobsCoordinationFixture>(fixture)
{
    [Fact]
    public override Task keyed_provider_operation_matrix_survives_restart() =>
        base.keyed_provider_operation_matrix_survives_restart();

    [Fact]
    public override Task keyed_constraints_follow_custom_column_mappings() =>
        base.keyed_constraints_follow_custom_column_mappings();

    [Fact]
    public override Task fresh_schema_enforces_keyed_metadata_and_scoped_uniqueness() =>
        base.fresh_schema_enforces_keyed_metadata_and_scoped_uniqueness();

    [Fact]
    public override Task manual_job_configuration_requires_explicit_ordinal_scope() =>
        base.manual_job_configuration_requires_explicit_ordinal_scope();

    [Fact]
    public override Task coordinated_manual_nonordinal_model_rejects_keyed_operations_before_middleware() =>
        base.coordinated_manual_nonordinal_model_rejects_keyed_operations_before_middleware();

    [Fact]
    public override Task manual_ordinal_job_configuration_preserves_key_scopes() =>
        base.manual_ordinal_job_configuration_preserves_key_scopes();

    [Fact]
    public override Task coordinated_add_rejects_retained_keyed_parent_before_batch_effects() =>
        base.coordinated_add_rejects_retained_keyed_parent_before_batch_effects();
}
