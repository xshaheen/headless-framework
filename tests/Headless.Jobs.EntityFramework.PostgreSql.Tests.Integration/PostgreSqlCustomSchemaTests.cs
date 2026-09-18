// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

[Collection<PostgreSqlJobsCoordinationFixture>]
public sealed class PostgreSqlCustomSchemaTests(PostgreSqlJobsCoordinationFixture fixture)
    : JobsCustomSchemaConformanceTests<PostgreSqlJobsCoordinationFixture>(fixture)
{
    [Fact]
    public override Task configured_schema_holds_every_jobs_table_including_the_reservation_table() =>
        base.configured_schema_holds_every_jobs_table_including_the_reservation_table();
}
