// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless;
using Headless.Jobs;
using Headless.Jobs.Provider;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Provider;

/// <summary>
/// The in-memory claims read a root, then take it in a later step. A root rescheduled between the two must not run
/// early, whether the edit kept its UpdatedAt or moved it back: neither stamp proves the row is unchanged, so each
/// claim re-checks the stored row's due time. The fallback sweep takes only overdue roots; the direct claim takes a
/// root once its ExecutionTime is at most one second away.
/// </summary>
public sealed class FallbackClaimRescheduleProviderTests : TestBase
{
    private sealed class FakeTimeJob : TimeJobEntity<FakeTimeJob>;

    private sealed class FakeCronJob : CronJobEntity;

    private static readonly DateTime _Now = new(2026, 06, 17, 12, 00, 00, DateTimeKind.Utc);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task should_not_claim_a_root_rescheduled_after_the_peek(bool lowerStamp)
    {
        var provider = _CreateProvider();

        var first = _DueRoot(_Now.AddMinutes(-10));
        var second = _DueRoot(_Now.AddMinutes(-5));
        await provider.AddTimeJobsAsync([first, second], AbortToken);

        await using var sweep = provider.QueueTimedOutTimeJobsAsync(AbortToken).GetAsyncEnumerator(AbortToken);
        (await sweep.MoveNextAsync()).Should().BeTrue();
        sweep.Current.Id.Should().Be(first.Id);

        var rescheduled = await provider.GetTimeJobByIdAsync(second.Id, AbortToken);
        rescheduled!.ExecutionTime = _Now.AddHours(1);
        if (lowerStamp)
        {
            rescheduled.UpdatedAt = rescheduled.UpdatedAt.AddSeconds(-1);
        }

        (await provider.UpdateTimeJobsAsync([rescheduled], AbortToken)).Should().Be(1);

        (await sweep.MoveNextAsync()).Should().BeFalse();
        var stored = await provider.GetTimeJobByIdAsync(second.Id, AbortToken);
        stored!.Status.Should().Be(JobStatus.Idle);
        stored.OwnerId.Should().BeNull();
        stored.LockedUntil.Should().BeNull();
    }

    [Theory]
    [InlineData(-60_000, true)]
    [InlineData(500, true)]
    [InlineData(1_000, true)]
    [InlineData(1_001, false)]
    [InlineData(600_000, false)]
    public async Task should_claim_a_root_directly_only_when_it_is_due(int dueInMilliseconds, bool expectedClaim)
    {
        var provider = _CreateProvider();
        var root = _DueRoot(_Now.AddMilliseconds(dueInMilliseconds));
        await provider.AddTimeJobsAsync([root], AbortToken);
        var candidate = await _ReadCandidateAsync(provider, root.Id);

        var claimed = await provider.QueueTimeJobsAsync([candidate], AbortToken).ToArrayAsync(AbortToken);

        Guid[] expectedIds = expectedClaim ? [root.Id] : [];
        claimed.Select(x => x.Id).Should().Equal(expectedIds);
        var stored = await provider.GetTimeJobByIdAsync(root.Id, AbortToken);
        stored!.Status.Should().Be(expectedClaim ? JobStatus.Queued : JobStatus.Idle);
    }

    [Fact]
    public async Task should_not_directly_claim_a_root_rescheduled_after_it_was_read()
    {
        var provider = _CreateProvider();
        var root = _DueRoot(_Now.AddMinutes(-1));
        await provider.AddTimeJobsAsync([root], AbortToken);
        var candidate = await _ReadCandidateAsync(provider, root.Id);

        var rescheduled = await provider.GetTimeJobByIdAsync(root.Id, AbortToken);
        rescheduled!.ExecutionTime = _Now.AddHours(1);
        (await provider.UpdateTimeJobsAsync([rescheduled], AbortToken)).Should().Be(1);
        // The frozen clock stamps the edit with the stamp the candidate already carries.
        (await provider.GetTimeJobByIdAsync(root.Id, AbortToken))!
            .UpdatedAt.Should()
            .Be(candidate.UpdatedAt);

        var claimed = await provider.QueueTimeJobsAsync([candidate], AbortToken).ToArrayAsync(AbortToken);

        claimed.Should().BeEmpty();
        var stored = await provider.GetTimeJobByIdAsync(root.Id, AbortToken);
        stored!.Status.Should().Be(JobStatus.Idle);
        stored.OwnerId.Should().BeNull();
        stored.LockedUntil.Should().BeNull();
    }

    // The scheduler's peek hands the claim the row's id, function, and the UpdatedAt it read.
    private static async Task<TimeJobEntity> _ReadCandidateAsync(
        JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob> provider,
        Guid id
    )
    {
        var stored = await provider.GetTimeJobByIdAsync(id, AbortToken);
        return new TimeJobEntity
        {
            Id = stored!.Id,
            Function = stored.Function,
            UpdatedAt = stored.UpdatedAt,
        };
    }

    private static JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob> _CreateProvider()
    {
        // The clock is never advanced, so an unchanged stamp collides with the read one exactly.
        var time = new FakeTimeProvider(new DateTimeOffset(_Now, TimeSpan.Zero));
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(time);
        services.AddSingleton<IGuidGenerator>(new SequentialGuidGenerator(SequentialGuidType.Version7));
        services.AddSingleton(
            new SchedulerOptionsBuilder { NodeId = "node-a", LeaseDuration = TimeSpan.FromMinutes(5) }
        );
        return new JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob>(services.BuildServiceProvider());
    }

    private static FakeTimeJob _DueRoot(DateTime executionTime)
    {
        return new FakeTimeJob
        {
            Id = Guid.NewGuid(),
            Function = "fn",
            Status = JobStatus.Idle,
            OnNodeDeath = NodeDeathPolicy.Retry,
            ExecutionTime = executionTime,
            // Seeded straight into the provider, so nothing stamps it; the stamp-moved-back case needs a real value.
            UpdatedAt = new DateTimeOffset(_Now, TimeSpan.Zero),
        };
    }
}
