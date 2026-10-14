// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless;
using Headless.Jobs;
using Headless.Jobs.Provider;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Provider;

/// <summary>
/// The in-memory fallback sweep peeks its due roots, then claims them one at a time as the caller enumerates. A root
/// rescheduled between the peek and its claim must not run early, whether the edit kept its UpdatedAt or moved it
/// back: neither stamp proves the row is unchanged, so the claim re-checks the due time.
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
        // The clock is never advanced, so an unchanged stamp collides with the peeked one exactly.
        var time = new FakeTimeProvider(new DateTimeOffset(_Now, TimeSpan.Zero));
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(time);
        services.AddSingleton<IGuidGenerator>(new SequentialGuidGenerator(SequentialGuidType.Version7));
        services.AddSingleton(
            new SchedulerOptionsBuilder { NodeId = "node-a", LeaseDuration = TimeSpan.FromMinutes(5) }
        );
        var provider = new JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob>(services.BuildServiceProvider());

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
