// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

[Collection<SqlServerJobsCoordinationFixture>]
public sealed class SqlServerCustomSchemaTests(SqlServerJobsCoordinationFixture fixture)
    : JobsCustomSchemaConformanceTests<SqlServerJobsCoordinationFixture>(fixture)
{
    [Fact]
    public override Task configured_schema_holds_every_jobs_table_including_the_reservation_table() =>
        base.configured_schema_holds_every_jobs_table_including_the_reservation_table();
}
