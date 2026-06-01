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
}
