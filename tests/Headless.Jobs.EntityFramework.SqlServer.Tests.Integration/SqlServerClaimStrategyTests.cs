// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.DbContextFactory;
using Headless.Jobs.Entities;
using Headless.Jobs.Infrastructure;
using Headless.Testing.Tests;

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

[Collection<SqlServerJobsCoordinationFixture>]
public sealed class SqlServerClaimStrategyTests : TestBase
{
    [Fact]
    public void rcsi_hint_includes_readcommittedlock()
    {
        SqlServerJobsClaimStrategy<JobsDbContext, TimeJobEntity, CronJobEntity>
            .GetReadPastHints(readCommittedSnapshotEnabled: true)
            .Should()
            .Contain("READCOMMITTEDLOCK");
    }
}
