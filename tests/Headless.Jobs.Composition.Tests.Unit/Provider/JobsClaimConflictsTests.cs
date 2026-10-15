// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Jobs.Infrastructure;
using Headless.Testing.Tests;

namespace Tests.Provider;

public sealed class JobsClaimConflictsTests : TestBase
{
    [Fact]
    public void should_count_unclaimed_candidates_when_claim_stayed_under_its_budget()
    {
        var candidates = _Candidates(10);
        var claimed = candidates.Take(7).ToDictionary(x => x.Id, _ => DateTimeOffset.UnixEpoch);

        var lost = JobsClaimConflicts.CountLost(candidates, claimed, JobsRunFilter.All, JobsClusterConcurrency.None);

        lost.Should().Be(3);
    }

    [Fact]
    public void should_count_nothing_when_claim_filled_its_budget()
    {
        var candidates = _Candidates(JobsClaimStrategyDefaults.MaxClaimBatchSize + 50);
        var claimed = candidates
            .Take(JobsClaimStrategyDefaults.MaxClaimBatchSize)
            .ToDictionary(x => x.Id, _ => DateTimeOffset.UnixEpoch);

        var lost = JobsClaimConflicts.CountLost(candidates, claimed, JobsRunFilter.All, JobsClusterConcurrency.None);

        lost.Should().Be(0);
    }

    private static TimeJobEntity[] _Candidates(int count)
    {
        return [.. Enumerable.Range(0, count).Select(_ => new TimeJobEntity { Id = Guid.NewGuid(), Function = "fn" })];
    }
}
