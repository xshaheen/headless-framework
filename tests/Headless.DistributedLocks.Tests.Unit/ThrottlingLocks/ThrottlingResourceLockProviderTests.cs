// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Tests.Fakes;

namespace Tests.ThrottlingLocks;

public sealed class ThrottlingResourceLockProviderTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly FakeThrottlingResourceLockStorage _storage = new();
    private readonly ILogger<ThrottlingResourceLockProvider> _logger;

    public ThrottlingResourceLockProviderTests()
    {
        _logger = LoggerFactory.CreateLogger<ThrottlingResourceLockProvider>();
    }

    private ThrottlingResourceLockProvider _CreateProvider(ThrottlingResourceLockOptions? options = null)
    {
        options ??= new ThrottlingResourceLockOptions { MaxHitsPerPeriod = 3, ThrottlingPeriod = TimeSpan.FromMinutes(1) };
        return new ThrottlingResourceLockProvider(_storage, options, _timeProvider, _logger);
    }

    #region TryAcquireAsync

    [Fact]
    public async Task should_acquire_when_under_limit()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);

        // when
        var result = await provider.TryAcquireAsync(resource, cancellationToken: AbortToken);

        // then
        result.Should().NotBeNull();
        result!.Resource.Should().Be(resource);
        result.DateAcquired.Should().BeCloseTo(_timeProvider.GetUtcNow(), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task should_return_null_when_at_limit()
    {
        // given
        var options = new ThrottlingResourceLockOptions
        {
            MaxHitsPerPeriod = 2,
            ThrottlingPeriod = TimeSpan.FromMinutes(1),
        };
        var provider = _CreateProvider(options);
        var resource = Faker.Random.AlphaNumeric(10);

        // Acquire until limit reached
        await provider.TryAcquireAsync(resource, TimeSpan.FromMilliseconds(100), AbortToken);
        await provider.TryAcquireAsync(resource, TimeSpan.FromMilliseconds(100), AbortToken);

        // when - try to acquire one more (should timeout immediately)
        var result = await provider.TryAcquireAsync(resource, TimeSpan.FromMilliseconds(50), AbortToken);

        // then
        result.Should().BeNull();
    }

    [Fact]
    public async Task should_wait_and_retry_when_at_limit()
    {
        // given
        var options = new ThrottlingResourceLockOptions
        {
            MaxHitsPerPeriod = 1,
            ThrottlingPeriod = TimeSpan.FromMinutes(1),
        };
        var provider = _CreateProvider(options);
        var resource = Faker.Random.AlphaNumeric(10);

        // Exhaust the limit
        await provider.TryAcquireAsync(resource, TimeSpan.FromMilliseconds(100), AbortToken);

        // when - start acquisition that will wait, then advance time past period
        var acquireTask = Task.Run(async () =>
        {
            return await provider.TryAcquireAsync(resource, TimeSpan.FromMinutes(2), AbortToken);
        });

        // Give the task time to start waiting
        await Task.Delay(50, AbortToken);

        // Advance time past the throttling period to trigger retry
        _timeProvider.Advance(TimeSpan.FromMinutes(1).Add(TimeSpan.FromMilliseconds(10)));

        var result = await acquireTask;

        // then - should have acquired after period reset
        result.Should().NotBeNull();
        result!.Resource.Should().Be(resource);
    }

    [Fact]
    public async Task should_release_decrements_count()
    {
        // given - throttling locks don't have explicit release; slots free when TTL expires
        var options = new ThrottlingResourceLockOptions
        {
            MaxHitsPerPeriod = 1,
            ThrottlingPeriod = TimeSpan.FromSeconds(2),
        };
        var provider = _CreateProvider(options);
        var resource = Faker.Random.AlphaNumeric(10);

        // Acquire the single slot
        var first = await provider.TryAcquireAsync(resource, TimeSpan.FromMilliseconds(100), AbortToken);
        first.Should().NotBeNull();

        // Verify locked
        var isLocked = await provider.IsLockedAsync(resource);
        isLocked.Should().BeTrue();

        // when - advance time past the throttling period (slot expires)
        _timeProvider.Advance(TimeSpan.FromSeconds(3));
        _storage.Clear(); // Simulate TTL expiration in storage

        // then - should be able to acquire again
        var second = await provider.TryAcquireAsync(resource, TimeSpan.FromMilliseconds(100), AbortToken);
        second.Should().NotBeNull();
    }

    [Fact]
    public async Task should_expire_slots_after_ttl()
    {
        // given
        var options = new ThrottlingResourceLockOptions
        {
            MaxHitsPerPeriod = 2,
            ThrottlingPeriod = TimeSpan.FromSeconds(1),
        };
        var provider = _CreateProvider(options);
        var resource = Faker.Random.AlphaNumeric(10);

        // Fill up all slots
        await provider.TryAcquireAsync(resource, TimeSpan.FromMilliseconds(100), AbortToken);
        await provider.TryAcquireAsync(resource, TimeSpan.FromMilliseconds(100), AbortToken);

        // Verify locked
        (await provider.IsLockedAsync(resource)).Should().BeTrue();

        // when - advance time and clear storage to simulate expiration
        _timeProvider.Advance(TimeSpan.FromSeconds(2));
        _storage.Clear();

        // then - slots should be freed
        var isLocked = await provider.IsLockedAsync(resource);
        isLocked.Should().BeFalse();

        // Can acquire again
        var result = await provider.TryAcquireAsync(resource, TimeSpan.FromMilliseconds(100), AbortToken);
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task should_get_available_slots()
    {
        // given - IsLockedAsync returns false when slots available, true when at limit
        var options = new ThrottlingResourceLockOptions
        {
            MaxHitsPerPeriod = 3,
            ThrottlingPeriod = TimeSpan.FromMinutes(1),
        };
        var provider = _CreateProvider(options);
        var resource = Faker.Random.AlphaNumeric(10);

        // Initially not locked (3 slots available)
        var initialLocked = await provider.IsLockedAsync(resource);
        initialLocked.Should().BeFalse();

        // Acquire 2 slots (1 remaining)
        await provider.TryAcquireAsync(resource, TimeSpan.FromMilliseconds(100), AbortToken);
        await provider.TryAcquireAsync(resource, TimeSpan.FromMilliseconds(100), AbortToken);

        // Still not locked (1 slot available)
        var partialLocked = await provider.IsLockedAsync(resource);
        partialLocked.Should().BeFalse();

        // Acquire last slot
        await provider.TryAcquireAsync(resource, TimeSpan.FromMilliseconds(100), AbortToken);

        // when - all slots used
        var fullyLocked = await provider.IsLockedAsync(resource);

        // then
        fullyLocked.Should().BeTrue();
    }

    #endregion

    protected override ValueTask DisposeAsyncCore()
    {
        _storage.Clear();
        return base.DisposeAsyncCore();
    }
}
