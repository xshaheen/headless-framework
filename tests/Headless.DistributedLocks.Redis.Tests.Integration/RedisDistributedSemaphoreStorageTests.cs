// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks.Redis;
using Headless.Redis;
using Headless.Testing.Tests;

namespace Tests;

/// <summary>Integration tests for <see cref="RedisDistributedSemaphoreStorage"/>.</summary>
[Collection<RedisTestFixture>]
public sealed class RedisDistributedSemaphoreStorageTests(RedisTestFixture fixture) : TestBase
{
    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await fixture.ConnectionMultiplexer.FlushAllAsync();
    }

    [Fact]
    public async Task should_allow_up_to_max_count_holders()
    {
        // given
        var resource = $"semaphore:{Faker.Random.AlphaNumeric(10)}";

        // when
        var first = await fixture.SemaphoreStorage.TryAcquireAsync(resource, "lock-1", 2, TimeSpan.FromMinutes(5), AbortToken);
        var second = await fixture.SemaphoreStorage.TryAcquireAsync(resource, "lock-2", 2, TimeSpan.FromMinutes(5), AbortToken);
        var third = await fixture.SemaphoreStorage.TryAcquireAsync(resource, "lock-3", 2, TimeSpan.FromMinutes(5), AbortToken);

        // then
        first.Acquired.Should().BeTrue();
        second.Acquired.Should().BeTrue();
        third.Acquired.Should().BeFalse();
        (await fixture.SemaphoreStorage.GetCountAsync(resource, AbortToken)).Should().Be(2);
    }

    [Fact]
    public async Task should_reacquire_after_release_and_advance_fencing_token()
    {
        // given
        var resource = $"semaphore:{Faker.Random.AlphaNumeric(10)}";

        // when
        var first = await fixture.SemaphoreStorage.TryAcquireAsync(resource, "lock-1", 1, TimeSpan.FromMinutes(5), AbortToken);
        var released = await fixture.SemaphoreStorage.ReleaseAsync(resource, "lock-1", AbortToken);
        var second = await fixture.SemaphoreStorage.TryAcquireAsync(resource, "lock-2", 1, TimeSpan.FromMinutes(5), AbortToken);

        // then
        first.FencingToken.Should().Be(1);
        released.Should().BeTrue();
        second.FencingToken.Should().Be(2);
    }

    [Fact]
    public async Task should_reacquire_after_slot_expiry()
    {
        // given
        var resource = $"semaphore:{Faker.Random.AlphaNumeric(10)}";
        await fixture.SemaphoreStorage.TryAcquireAsync(resource, "lock-1", 1, TimeSpan.FromMilliseconds(100), AbortToken);

        // when
        await Task.Delay(250, AbortToken);
        var second = await fixture.SemaphoreStorage.TryAcquireAsync(resource, "lock-2", 1, TimeSpan.FromMinutes(5), AbortToken);

        // then
        second.Acquired.Should().BeTrue();
    }

    [Fact]
    public async Task should_extend_existing_slot_without_readding_expired_slot()
    {
        // given
        var resource = $"semaphore:{Faker.Random.AlphaNumeric(10)}";
        await fixture.SemaphoreStorage.TryAcquireAsync(resource, "lock-1", 1, TimeSpan.FromMilliseconds(100), AbortToken);

        // when
        await Task.Delay(250, AbortToken);
        var extended = await fixture.SemaphoreStorage.TryExtendAsync(resource, "lock-1", TimeSpan.FromMinutes(5), AbortToken);

        // then
        extended.Should().BeFalse();
        (await fixture.SemaphoreStorage.GetCountAsync(resource, AbortToken)).Should().Be(0);
    }

    [Fact]
    public async Task should_validate_live_holder()
    {
        // given
        var resource = $"semaphore:{Faker.Random.AlphaNumeric(10)}";
        await fixture.SemaphoreStorage.TryAcquireAsync(resource, "lock-1", 1, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var valid = await fixture.SemaphoreStorage.ValidateAsync(resource, "lock-1", AbortToken);

        // then
        valid.Should().BeTrue();
    }

    [Fact]
    public async Task should_not_shrink_holders_key_ttl_when_shorter_holder_acquires()
    {
        // given
        var resource = $"semaphore:{Faker.Random.AlphaNumeric(10)}";
        var holdersKey = _GetHoldersKey(resource);
        await fixture.SemaphoreStorage.TryAcquireAsync(resource, "lock-1", 2, TimeSpan.FromSeconds(5), AbortToken);

        // when
        var second = await fixture.SemaphoreStorage.TryAcquireAsync(
            resource,
            "lock-2",
            2,
            TimeSpan.FromMilliseconds(100),
            AbortToken
        );

        // then
        second.Acquired.Should().BeTrue();
        var ttl = await fixture.ConnectionMultiplexer.GetDatabase().KeyTimeToLiveAsync(holdersKey);
        ttl.Should().NotBeNull();
        ttl!.Value.Should().BeGreaterThan(TimeSpan.FromSeconds(4));
    }

    [Fact]
    public async Task should_not_shrink_holders_key_ttl_when_shorter_holder_extends()
    {
        // given
        var resource = $"semaphore:{Faker.Random.AlphaNumeric(10)}";
        var holdersKey = _GetHoldersKey(resource);
        await fixture.SemaphoreStorage.TryAcquireAsync(resource, "lock-1", 2, TimeSpan.FromSeconds(5), AbortToken);
        await fixture.SemaphoreStorage.TryAcquireAsync(resource, "lock-2", 2, TimeSpan.FromSeconds(5), AbortToken);

        // when
        var extended = await fixture.SemaphoreStorage.TryExtendAsync(
            resource,
            "lock-2",
            TimeSpan.FromMilliseconds(100),
            AbortToken
        );

        // then
        extended.Should().BeTrue();
        var ttl = await fixture.ConnectionMultiplexer.GetDatabase().KeyTimeToLiveAsync(holdersKey);
        ttl.Should().NotBeNull();
        ttl!.Value.Should().BeGreaterThan(TimeSpan.FromSeconds(4));
    }

    [Fact]
    public async Task should_extend_live_slot_and_survive_past_original_ttl()
    {
        // given
        var resource = $"semaphore:{Faker.Random.AlphaNumeric(10)}";
        var holdersKey = _GetHoldersKey(resource);
        var ttl = TimeSpan.FromSeconds(2);
        await fixture.SemaphoreStorage.TryAcquireAsync(resource, "lock-1", 1, ttl, AbortToken);

        // when — extend before the original TTL elapses
        var extended = await fixture.SemaphoreStorage.TryExtendAsync(resource, "lock-1", TimeSpan.FromSeconds(10), AbortToken);

        // then — TryExtendAsync returns true and the score in the ZSET is beyond the original TTL
        extended.Should().BeTrue();
        var keyTtl = await fixture.ConnectionMultiplexer.GetDatabase().KeyTimeToLiveAsync(holdersKey);
        keyTtl.Should().NotBeNull();
        keyTtl!.Value.Should().BeGreaterThan(ttl);
    }

    [Fact]
    public async Task should_allow_exactly_max_count_concurrent_holders_under_parallel_load()
    {
        // given
        var resource = $"semaphore:{Faker.Random.AlphaNumeric(10)}";
        const int maxCount = 5;
        const int totalCandidates = 10;
        var successCount = 0;

        // when — 10 tasks all race to acquire a semaphore with capacity 5
        var lockIds = Enumerable.Range(1, totalCandidates).Select(i => $"lock-{i}").ToList();
        await Parallel.ForEachAsync(
            lockIds,
            new ParallelOptions { MaxDegreeOfParallelism = totalCandidates },
            async (lockId, _) =>
            {
                var result = await fixture.SemaphoreStorage.TryAcquireAsync(
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

        // then — exactly max_count tasks succeeded
        successCount.Should().Be(maxCount);
        (await fixture.SemaphoreStorage.GetCountAsync(resource, AbortToken)).Should().Be(maxCount);
    }

    [Fact]
    public async Task should_not_advance_fencing_token_on_failed_capacity_rejected_acquire()
    {
        // given — fill capacity
        var resource = $"semaphore:{Faker.Random.AlphaNumeric(10)}";
        var first = await fixture.SemaphoreStorage.TryAcquireAsync(resource, "lock-1", 1, TimeSpan.FromMinutes(5), AbortToken);

        // when — rejected acquire (capacity full)
        var rejected = await fixture.SemaphoreStorage.TryAcquireAsync(resource, "lock-2", 1, TimeSpan.FromMinutes(5), AbortToken);

        // release first slot and acquire again
        await fixture.SemaphoreStorage.ReleaseAsync(resource, "lock-1", AbortToken);
        var second = await fixture.SemaphoreStorage.TryAcquireAsync(resource, "lock-3", 1, TimeSpan.FromMinutes(5), AbortToken);

        // then — fencing token is strictly +1 from the last success (rejected attempt did not advance counter)
        first.FencingToken.Should().Be(1);
        rejected.Acquired.Should().BeFalse();
        rejected.FencingToken.Should().BeNull();
        second.FencingToken.Should().Be(2);
    }

    [Fact]
    public async Task should_set_holders_key_safety_ttl_approximately_double_slot_ttl()
    {
        // given
        var resource = $"semaphore:{Faker.Random.AlphaNumeric(10)}";
        var holdersKey = _GetHoldersKey(resource);
        var slotTtl = TimeSpan.FromSeconds(10);

        // when
        await fixture.SemaphoreStorage.TryAcquireAsync(resource, "lock-1", 1, slotTtl, AbortToken);

        // then — the ZSET key's TTL must be in the range (slotTtl, 2 * slotTtl]
        var keyTtl = await fixture.ConnectionMultiplexer.GetDatabase().KeyTimeToLiveAsync(holdersKey);
        keyTtl.Should().NotBeNull();
        keyTtl!.Value.Should().BeGreaterThan(slotTtl);
        keyTtl!.Value.Should().BeLessThanOrEqualTo(slotTtl * 2);
    }

    [Fact]
    public async Task should_exclude_expired_but_unpruned_slot_from_count_and_validate()
    {
        // given — plant a ZSET member directly with a past expiry score (an expired-but-unpruned
        // slot). We do NOT call TryAcquireAsync, which would prune via ZREMRANGEBYSCORE. This proves
        // the now-read-only Validate/Count scripts exclude a stale slot without mutating state.
        var resource = $"semaphore:{Faker.Random.AlphaNumeric(10)}";
        var holdersKey = _GetHoldersKey(resource);
        const string lockId = "expired-lock-1";
        var db = fixture.ConnectionMultiplexer.GetDatabase();
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var pastExpiryScore = nowMs - 60_000; // expired one minute ago
        await db.SortedSetAddAsync(holdersKey, lockId, pastExpiryScore);

        // when
        var count = await fixture.SemaphoreStorage.GetCountAsync(resource, AbortToken);
        var valid = await fixture.SemaphoreStorage.ValidateAsync(resource, lockId, AbortToken);

        // then — the stale slot is excluded from both the live count and the ownership check.
        count.Should().Be(0);
        valid.Should().BeFalse();
    }

    [Fact]
    public async Task should_acquire_with_thirty_day_ttl_beyond_int_overflow_boundary()
    {
        // given — 30 days in milliseconds exceeds int.MaxValue (~24.8 days), so the expiry score must
        // be stored as a long, not an int. This guards the int→long score fix.
        var resource = $"semaphore:{Faker.Random.AlphaNumeric(10)}";
        var holdersKey = _GetHoldersKey(resource);
        const string lockId = "long-ttl-lock";
        var ttl = TimeSpan.FromDays(30);

        // when
        var result = await fixture.SemaphoreStorage.TryAcquireAsync(resource, lockId, 1, ttl, AbortToken);

        // then — acquisition succeeds and the stored ZSET score is a large positive value beyond the
        // int-overflow boundary (a wrapped int would be negative or near-now).
        result.Acquired.Should().BeTrue();
        var db = fixture.ConnectionMultiplexer.GetDatabase();
        var score = await db.SortedSetScoreAsync(holdersKey, lockId);
        score.Should().NotBeNull();
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        // The score (now + 30d in ms) must be well past int.MaxValue and clearly in the future.
        score!.Value.Should().BeGreaterThan(int.MaxValue);
        score!.Value.Should().BeGreaterThan(nowMs + TimeSpan.FromDays(29).TotalMilliseconds);
    }

    private static string _GetHoldersKey(string resource)
    {
        return "{" + resource + "}:holders";
    }
}
