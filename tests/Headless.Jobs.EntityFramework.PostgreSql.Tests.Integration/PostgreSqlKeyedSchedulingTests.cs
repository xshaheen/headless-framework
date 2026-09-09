// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;

namespace Tests;

[Collection<PostgreSqlJobsCoordinationFixture>]
public sealed class PostgreSqlKeyedSchedulingTests(PostgreSqlJobsCoordinationFixture fixture)
    : JobsKeyedSchedulingConformanceTests<PostgreSqlJobsCoordinationFixture>(fixture)
{
    protected override void ConfigureRetry(DbContextOptionsBuilder options) =>
        options.UseNpgsql(Fixture.ConnectionString, provider => provider.EnableRetryOnFailure(1, TimeSpan.Zero, null));

    [Fact]
    public override Task keyed_operations_support_retry_enabled_contexts() =>
        base.keyed_operations_support_retry_enabled_contexts();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public override Task keyed_retry_restores_candidate_and_custom_properties(bool replace) =>
        base.keyed_retry_restores_candidate_and_custom_properties(replace);

    [Theory]
    [InlineData("schedule", false)]
    [InlineData("schedule", true)]
    [InlineData("replace", false)]
    [InlineData("replace", true)]
    [InlineData("cancel", false)]
    [InlineData("cancel", true)]
    public override Task keyed_commit_fault_is_not_replayed(string operation, bool afterCommit) =>
        base.keyed_commit_fault_is_not_replayed(operation, afterCommit);

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
    public override Task manual_keyed_constraints_follow_custom_column_mappings() =>
        base.manual_keyed_constraints_follow_custom_column_mappings();

    [Fact]
    public override Task manual_keyed_configuration_requires_finalization() =>
        base.manual_keyed_configuration_requires_finalization();

    [Fact]
    public override Task coordinated_add_rejects_retained_keyed_parent_before_batch_effects() =>
        base.coordinated_add_rejects_retained_keyed_parent_before_batch_effects();
}
