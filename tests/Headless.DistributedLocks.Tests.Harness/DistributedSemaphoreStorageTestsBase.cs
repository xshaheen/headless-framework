// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Testing.Tests;

namespace Tests;

public abstract class DistributedSemaphoreStorageTestsBase : TestBase
{
    protected abstract IDistributedSemaphoreStorage SemaphoreStorage { get; }
    protected abstract TimeProvider TimeProvider { get; }
    protected abstract Task AdvanceTimeAsync(TimeSpan amount, CancellationToken cancellationToken);

    public virtual async Task should_allow_up_to_max_count_holders()
    {
        var resource = Faker.Random.AlphaNumeric(10);

        var first = await SemaphoreStorage.TryAcquireAsync(resource, "lock-1", 2, TimeSpan.FromMinutes(5), AbortToken);
        var second = await SemaphoreStorage.TryAcquireAsync(resource, "lock-2", 2, TimeSpan.FromMinutes(5), AbortToken);
        var third = await SemaphoreStorage.TryAcquireAsync(resource, "lock-3", 2, TimeSpan.FromMinutes(5), AbortToken);

        first.Acquired.Should().BeTrue();
        second.Acquired.Should().BeTrue();
        third.Acquired.Should().BeFalse();
        (await SemaphoreStorage.GetCountAsync(resource, AbortToken)).Should().Be(2);
    }

    public virtual async Task should_not_exceed_max_count_under_concurrent_acquires()
    {
        var resource = Faker.Random.AlphaNumeric(10);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable
            .Range(0, 64)
            .Select(async index =>
            {
                await start.Task.WaitAsync(AbortToken);

                return await SemaphoreStorage.TryAcquireAsync(
                    resource,
                    $"lock-{index}",
                    maxCount: 1,
                    TimeSpan.FromMinutes(5),
                    AbortToken
                );
            })
            .ToArray();

        start.SetResult();

        var results = await Task.WhenAll(tasks);

        results.Count(static result => result.Acquired).Should().Be(1);
        (await SemaphoreStorage.GetCountAsync(resource, AbortToken)).Should().Be(1);
    }

    public virtual async Task should_reacquire_after_release_and_advance_fencing_token()
    {
        var resource = Faker.Random.AlphaNumeric(10);

        var first = await SemaphoreStorage.TryAcquireAsync(resource, "lock-1", 1, TimeSpan.FromMinutes(5), AbortToken);
        var released = await SemaphoreStorage.ReleaseAsync(resource, "lock-1", AbortToken);
        var second = await SemaphoreStorage.TryAcquireAsync(resource, "lock-2", 1, TimeSpan.FromMinutes(5), AbortToken);

        first.FencingToken.Should().Be(1);
        released.Should().BeTrue();
        second.FencingToken.Should().Be(2);
    }

    public virtual async Task should_not_advance_fencing_token_on_capacity_rejected_acquire()
    {
        var resource = Faker.Random.AlphaNumeric(10);
        var first = await SemaphoreStorage.TryAcquireAsync(resource, "lock-1", 1, TimeSpan.FromMinutes(5), AbortToken);

        var rejected = await SemaphoreStorage.TryAcquireAsync(
            resource,
            "lock-2",
            1,
            TimeSpan.FromMinutes(5),
            AbortToken
        );

        await SemaphoreStorage.ReleaseAsync(resource, "lock-1", AbortToken);
        var second = await SemaphoreStorage.TryAcquireAsync(resource, "lock-3", 1, TimeSpan.FromMinutes(5), AbortToken);

        first.FencingToken.Should().Be(1);
        rejected.Acquired.Should().BeFalse();
        rejected.FencingToken.Should().BeNull();
        second.FencingToken.Should().Be(2);
    }

    public virtual async Task should_reacquire_after_slot_expiry()
    {
        var resource = Faker.Random.AlphaNumeric(10);
        await SemaphoreStorage.TryAcquireAsync(resource, "lock-1", 1, TimeSpan.FromMilliseconds(100), AbortToken);

        await AdvanceTimeAsync(TimeSpan.FromMilliseconds(250), AbortToken);
        var second = await SemaphoreStorage.TryAcquireAsync(resource, "lock-2", 1, TimeSpan.FromMinutes(5), AbortToken);

        second.Acquired.Should().BeTrue();
    }

    public virtual async Task should_extend_live_slot_and_not_re_add_expired_slot()
    {
        var resource = Faker.Random.AlphaNumeric(10);
        await SemaphoreStorage.TryAcquireAsync(resource, "lock-1", 1, TimeSpan.FromMilliseconds(100), AbortToken);

        await AdvanceTimeAsync(TimeSpan.FromMilliseconds(250), AbortToken);
        var extended = await SemaphoreStorage.TryExtendAsync(resource, "lock-1", TimeSpan.FromMinutes(5), AbortToken);

        extended.Should().BeFalse();
        (await SemaphoreStorage.GetCountAsync(resource, AbortToken)).Should().Be(0);
    }

    public virtual async Task should_not_shorten_live_slot_on_shorter_extend()
    {
        var resource = Faker.Random.AlphaNumeric(10);
        await SemaphoreStorage.TryAcquireAsync(resource, "lock-1", 1, TimeSpan.FromSeconds(2), AbortToken);

        // A shorter extend must not move the slot's expiry earlier (GREATEST semantics): after the shorter
        // extend's own window would have lapsed, the original longer lease still validates as live.
        var shorterExtend = await SemaphoreStorage.TryExtendAsync(
            resource,
            "lock-1",
            TimeSpan.FromMilliseconds(100),
            AbortToken
        );
        await AdvanceTimeAsync(TimeSpan.FromMilliseconds(400), AbortToken);
        var stillValid = await SemaphoreStorage.ValidateAsync(resource, "lock-1", AbortToken);

        shorterExtend.Should().BeTrue();
        stillValid.Should().BeTrue();
        (await SemaphoreStorage.GetCountAsync(resource, AbortToken)).Should().Be(1);
    }

    public virtual async Task should_validate_live_holder()
    {
        var resource = Faker.Random.AlphaNumeric(10);
        await SemaphoreStorage.TryAcquireAsync(resource, "lock-1", 1, TimeSpan.FromMinutes(5), AbortToken);

        var valid = await SemaphoreStorage.ValidateAsync(resource, "lock-1", AbortToken);

        valid.Should().BeTrue();
    }

    public virtual async Task should_exclude_expired_holder_from_count_and_validate()
    {
        var resource = Faker.Random.AlphaNumeric(10);
        await SemaphoreStorage.TryAcquireAsync(resource, "lock-1", 1, TimeSpan.FromMilliseconds(100), AbortToken);

        await AdvanceTimeAsync(TimeSpan.FromMilliseconds(250), AbortToken);

        var count = await SemaphoreStorage.GetCountAsync(resource, AbortToken);
        var valid = await SemaphoreStorage.ValidateAsync(resource, "lock-1", AbortToken);

        count.Should().Be(0);
        valid.Should().BeFalse();
    }

    public virtual async Task should_allow_exactly_max_count_concurrent_holders_under_parallel_load()
    {
        var resource = Faker.Random.AlphaNumeric(10);
        const int maxCount = 5;
        const int totalCandidates = 10;
        var successCount = 0;

        var lockIds = Enumerable.Range(1, totalCandidates).Select(i => $"lock-{i}").ToList();
        await Parallel.ForEachAsync(
            lockIds,
            new ParallelOptions { MaxDegreeOfParallelism = totalCandidates },
            async (lockId, _) =>
            {
                var result = await SemaphoreStorage.TryAcquireAsync(
                    resource,
                    lockId,
                    maxCount,
                    TimeSpan.FromMinutes(5),
                    AbortToken
                );

                if (result.Acquired)
                {
                    Interlocked.Increment(ref successCount);
                }
            }
        );

        successCount.Should().Be(maxCount);
        (await SemaphoreStorage.GetCountAsync(resource, AbortToken)).Should().Be(maxCount);
    }
}
