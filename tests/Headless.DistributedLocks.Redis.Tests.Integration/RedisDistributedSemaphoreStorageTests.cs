// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.DistributedLocks.Redis;
using Headless.Redis.Testing;

namespace Tests;

/// <summary>Integration tests for <see cref="RedisDistributedSemaphoreStorage"/>.</summary>
[Collection<RedisTestFixture>]
public sealed class RedisDistributedSemaphoreStorageTests(RedisTestFixture fixture)
    : DistributedSemaphoreStorageTestsBase
{
    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await fixture.ConnectionMultiplexer.FlushAllAsync();
    }

    protected override IDistributedSemaphoreStorage SemaphoreStorage => fixture.SemaphoreStorage;

    protected override TimeProvider TimeProvider => TimeProvider.System;

    protected override async Task AdvanceTimeAsync(TimeSpan amount, CancellationToken cancellationToken)
    {
        await Task.Delay(amount + TimeSpan.FromMilliseconds(50), TimeProvider.System, cancellationToken);
    }

    [Fact]
    public override Task should_allow_up_to_max_count_holders()
    {
        return base.should_allow_up_to_max_count_holders();
    }

    [Fact]
    public override Task should_not_exceed_max_count_under_concurrent_acquires()
    {
        return base.should_not_exceed_max_count_under_concurrent_acquires();
    }

    [Fact]
    public override Task should_reacquire_after_release_and_advance_fencing_token()
    {
        return base.should_reacquire_after_release_and_advance_fencing_token();
    }

    [Fact]
    public override Task should_not_advance_fencing_token_on_capacity_rejected_acquire()
    {
        return base.should_not_advance_fencing_token_on_capacity_rejected_acquire();
    }

    [Fact]
    public override Task should_reacquire_after_slot_expiry()
    {
        return base.should_reacquire_after_slot_expiry();
    }

    [Fact]
    public override Task should_extend_live_slot_and_not_re_add_expired_slot()
    {
        return base.should_extend_live_slot_and_not_re_add_expired_slot();
    }

    [Fact]
    public override Task should_not_shorten_live_slot_on_shorter_extend()
    {
        return base.should_not_shorten_live_slot_on_shorter_extend();
    }

    [Fact]
    public override Task should_validate_live_holder()
    {
        return base.should_validate_live_holder();
    }

    [Fact]
    public override Task should_exclude_expired_holder_from_count_and_validate()
    {
        return base.should_exclude_expired_holder_from_count_and_validate();
    }

    [Fact]
    public override Task should_allow_exactly_max_count_concurrent_holders_under_parallel_load()
    {
        return base.should_allow_exactly_max_count_concurrent_holders_under_parallel_load();
    }

    // Redis-specific tests stay in leaf:

    [Fact]
    public async Task should_not_shrink_holders_key_ttl_when_shorter_holder_acquires()
    {
        var resource = $"semaphore:{Faker.Random.AlphaNumeric(10)}";
        var holdersKey = _GetHoldersKey(resource);
        await fixture.SemaphoreStorage.TryAcquireAsync(resource, "lock-1", 2, TimeSpan.FromSeconds(5), AbortToken);

        var second = await fixture.SemaphoreStorage.TryAcquireAsync(
            resource,
            "lock-2",
            2,
            TimeSpan.FromMilliseconds(100),
            AbortToken
        );

        second.Acquired.Should().BeTrue();
        var ttl = await fixture.ConnectionMultiplexer.GetDatabase().KeyTimeToLiveAsync(holdersKey);
        ttl.Should().NotBeNull();
        ttl!.Value.Should().BeGreaterThan(TimeSpan.FromSeconds(4));
    }

    [Fact]
    public async Task should_not_shrink_holders_key_ttl_when_shorter_holder_extends()
    {
        var resource = $"semaphore:{Faker.Random.AlphaNumeric(10)}";
        var holdersKey = _GetHoldersKey(resource);
        await fixture.SemaphoreStorage.TryAcquireAsync(resource, "lock-1", 2, TimeSpan.FromSeconds(5), AbortToken);
        await fixture.SemaphoreStorage.TryAcquireAsync(resource, "lock-2", 2, TimeSpan.FromSeconds(5), AbortToken);

        var extended = await fixture.SemaphoreStorage.TryExtendAsync(
            resource,
            "lock-2",
            TimeSpan.FromMilliseconds(100),
            AbortToken
        );

        extended.Should().BeTrue();
        var ttl = await fixture.ConnectionMultiplexer.GetDatabase().KeyTimeToLiveAsync(holdersKey);
        ttl.Should().NotBeNull();
        ttl!.Value.Should().BeGreaterThan(TimeSpan.FromSeconds(4));
    }

    [Fact]
    public async Task should_set_holders_key_safety_ttl_approximately_double_slot_ttl()
    {
        var resource = $"semaphore:{Faker.Random.AlphaNumeric(10)}";
        var holdersKey = _GetHoldersKey(resource);
        var slotTtl = TimeSpan.FromSeconds(10);

        await fixture.SemaphoreStorage.TryAcquireAsync(resource, "lock-1", 1, slotTtl, AbortToken);

        var keyTtl = await fixture.ConnectionMultiplexer.GetDatabase().KeyTimeToLiveAsync(holdersKey);
        keyTtl.Should().NotBeNull();
        keyTtl!.Value.Should().BeGreaterThan(slotTtl);
        keyTtl!.Value.Should().BeLessThanOrEqualTo(slotTtl * 2);
    }

    [Fact]
    public async Task should_exclude_expired_but_unpruned_slot_from_count_and_validate()
    {
        var resource = $"semaphore:{Faker.Random.AlphaNumeric(10)}";
        var holdersKey = _GetHoldersKey(resource);
        const string leaseId = "expired-lock-1";
        var db = fixture.ConnectionMultiplexer.GetDatabase();
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var pastExpiryScore = nowMs - 60_000;
        await db.SortedSetAddAsync(holdersKey, leaseId, pastExpiryScore);

        var count = await fixture.SemaphoreStorage.GetCountAsync(resource, AbortToken);
        var valid = await fixture.SemaphoreStorage.ValidateAsync(resource, leaseId, AbortToken);

        count.Should().Be(0);
        valid.Should().BeFalse();
    }

    [Fact]
    public async Task should_acquire_with_thirty_day_ttl_beyond_int_overflow_boundary()
    {
        var resource = $"semaphore:{Faker.Random.AlphaNumeric(10)}";
        var holdersKey = _GetHoldersKey(resource);
        const string leaseId = "long-ttl-lock";
        var ttl = TimeSpan.FromDays(30);

        var result = await fixture.SemaphoreStorage.TryAcquireAsync(resource, leaseId, 1, ttl, AbortToken);

        result.Acquired.Should().BeTrue();
        var db = fixture.ConnectionMultiplexer.GetDatabase();
        var score = await db.SortedSetScoreAsync(holdersKey, leaseId);
        score.Should().NotBeNull();
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        score!.Value.Should().BeGreaterThan(int.MaxValue);
        score!.Value.Should().BeGreaterThan(nowMs + TimeSpan.FromDays(29).TotalMilliseconds);
    }

    private static string _GetHoldersKey(string resource)
    {
        return "{" + resource + "}:holders";
    }
}
