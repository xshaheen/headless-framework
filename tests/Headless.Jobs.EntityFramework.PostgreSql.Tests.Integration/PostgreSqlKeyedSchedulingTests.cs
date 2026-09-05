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
    public override Task public_job_configurations_create_valid_keyed_schema_without_customizer() =>
        base.public_job_configurations_create_valid_keyed_schema_without_customizer();
}
