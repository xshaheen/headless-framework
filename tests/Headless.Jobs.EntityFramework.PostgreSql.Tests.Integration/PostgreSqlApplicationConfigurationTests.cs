// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

[Collection<PostgreSqlJobsCoordinationFixture>]
public sealed class PostgreSqlApplicationConfigurationTests(PostgreSqlJobsCoordinationFixture fixture)
    : JobsApplicationConfigurationConformanceTests<PostgreSqlJobsCoordinationFixture>(fixture)
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public override Task application_message_and_scheduled_job_share_transaction(bool commit)
    {
        return base.application_message_and_scheduled_job_share_transaction(commit);
    }
}
