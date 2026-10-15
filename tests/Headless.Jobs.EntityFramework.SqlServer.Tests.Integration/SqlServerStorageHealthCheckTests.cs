// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

[Collection<SqlServerJobsCoordinationFixture>]
public sealed class SqlServerStorageHealthCheckTests(SqlServerJobsCoordinationFixture fixture)
    : JobsStorageHealthCheckConformanceTests<SqlServerJobsCoordinationFixture>(fixture)
{
    [Fact]
    public override Task storage_check_reports_healthy_when_the_store_can_connect() =>
        base.storage_check_reports_healthy_when_the_store_can_connect();

    [Fact]
    public override Task storage_check_reports_unhealthy_when_the_database_cannot_be_opened() =>
        base.storage_check_reports_unhealthy_when_the_database_cannot_be_opened();
}
