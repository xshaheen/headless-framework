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

    [Theory]
    [InlineData(false, true)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public override Task headless_application_context_shares_transaction(bool pooled, bool commit)
    {
        return base.headless_application_context_shares_transaction(pooled, commit);
    }
}
