// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

[Collection<SqlServerJobsCoordinationFixture>]
public sealed class SqlServerApplicationConfigurationTests(SqlServerJobsCoordinationFixture fixture)
    : JobsApplicationConfigurationConformanceTests<SqlServerJobsCoordinationFixture>(fixture)
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public override Task retrying_application_context_shares_transaction(bool commit)
    {
        return base.retrying_application_context_shares_transaction(commit);
    }

    [Fact]
    public override Task retrying_application_context_seeds_and_runs_jobs()
    {
        return base.retrying_application_context_seeds_and_runs_jobs();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public override Task retrying_application_context_enlists_in_connection_unit(bool commit)
    {
        return base.retrying_application_context_enlists_in_connection_unit(commit);
    }
}
