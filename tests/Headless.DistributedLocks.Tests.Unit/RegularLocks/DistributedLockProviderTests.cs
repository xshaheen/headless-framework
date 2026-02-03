// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.DistributedLocks;
using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Tests.Fakes;

namespace Tests.RegularLocks;

public sealed class DistributedLockProviderTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly FakeDistributedLockStorage _storage = new();
    private readonly IOutboxPublisher _outboxPublisher = Substitute.For<IOutboxPublisher>();
    private readonly ILongIdGenerator _longIdGenerator = Substitute.For<ILongIdGenerator>();

    private long _lockIdCounter = 1000;

    private DistributedLockProvider _CreateProvider(DistributedLockOptions? options = null)
    {
        options ??= new DistributedLockOptions();
        _longIdGenerator.Create().Returns(_ => Interlocked.Increment(ref _lockIdCounter));

        return new DistributedLockProvider(
            _storage,
            _outboxPublisher,
            options,
            _longIdGenerator,
            _timeProvider,
            LoggerFactory.CreateLogger<DistributedLockProvider>()
        );
    }

    #region TryAcquireAsync Tests

    [Fact]
    public async Task should_throw_when_resource_is_null()
    {
        // given
        var provider = _CreateProvider();

        // when
        var act = async () => await provider.TryAcquireAsync(null!, cancellationToken: AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("resource");
    }

    [Fact]
    public async Task should_throw_when_resource_is_whitespace()
    {
        // given
        var provider = _CreateProvider();

        // when
        var act = async () => await provider.TryAcquireAsync("   ", cancellationToken: AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("resource");
    }

    [Fact]
    public async Task should_throw_when_resource_exceeds_max_length()
    {
        // given
        var options = new DistributedLockOptions { MaxResourceNameLength = 10 };
        var provider = _CreateProvider(options);
        var longResource = new string('a', 11);

        // when
        var act = async () => await provider.TryAcquireAsync(longResource, cancellationToken: AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("resource");
    }

    [Fact]
    public async Task should_acquire_lock_when_not_held()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);

        // when
        var result = await provider.TryAcquireAsync(resource, cancellationToken: AbortToken);

        // then
        result.Should().NotBeNull();
        result!.Resource.Should().Be(resource);
        result.LockId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task should_return_null_when_already_locked()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);

        // Acquire first lock
        var firstLock = await provider.TryAcquireAsync(resource, cancellationToken: AbortToken);
        firstLock.Should().NotBeNull();

        // when - try to acquire second lock with zero timeout (immediate)
        var result = await provider.TryAcquireAsync(
            resource,
            acquireTimeout: TimeSpan.Zero,
            cancellationToken: AbortToken
        );

        // then
        result.Should().BeNull();
    }

    [Fact]
    public async Task should_retry_with_exponential_backoff()
    {
        // given
        var callCount = 0;
        var storage = Substitute.For<IDistributedLockStorage>();
        storage
            .InsertAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan?>())
            .Returns(callInfo =>
            {
                callCount++;
                // Succeed on 3rd attempt
                return ValueTask.FromResult(callCount >= 3);
            });

        var provider = new DistributedLockProvider(
            storage,
            _outboxPublisher,
            new DistributedLockOptions(),
            _longIdGenerator,
            _timeProvider,
            LoggerFactory.CreateLogger<DistributedLockProvider>()
        );
        var resource = Faker.Random.AlphaNumeric(10);

        // Start acquisition task
        var acquireTask = provider.TryAcquireAsync(
            resource,
            acquireTimeout: TimeSpan.FromSeconds(30),
            cancellationToken: AbortToken
        );

        // Advance time through backoff delays
        for (var i = 0; i < 5; i++)
        {
            await Task.Yield();
            _timeProvider.Advance(TimeSpan.FromMilliseconds(200));
        }

        // when
        var result = await acquireTask;

        // then - should have retried and succeeded
        result.Should().NotBeNull();
        callCount.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task should_return_null_after_acquire_timeout()
    {
        // given
        var options = new DistributedLockOptions();
        var provider = _CreateProvider(options);
        var resource = Faker.Random.AlphaNumeric(10);

        // Pre-lock the resource
        await _storage.InsertAsync(options.KeyPrefix + resource, "existing-lock", TimeSpan.FromMinutes(5));

        // when - use zero timeout for immediate failure
        var result = await provider.TryAcquireAsync(
            resource,
            acquireTimeout: TimeSpan.Zero,
            cancellationToken: AbortToken
        );

        // then
        result.Should().BeNull();
    }

    [Fact]
    public async Task should_respect_cancellation_token()
    {
        // given
        var options = new DistributedLockOptions();
        var provider = _CreateProvider(options);
        var resource = Faker.Random.AlphaNumeric(10);

        // Pre-lock the resource
        await _storage.InsertAsync(options.KeyPrefix + resource, "existing-lock", TimeSpan.FromMinutes(5));

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // when
        var act = async () =>
            await provider.TryAcquireAsync(
                resource,
                acquireTimeout: TimeSpan.FromMinutes(1),
                cancellationToken: cts.Token
            );

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_use_default_time_until_expires()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);

        // when
        var result = await provider.TryAcquireAsync(resource, cancellationToken: AbortToken);

        // then
        result.Should().NotBeNull();
        provider.DefaultTimeUntilExpires.Should().Be(TimeSpan.FromMinutes(20));
    }

    [Fact]
    public async Task should_use_custom_time_until_expires()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);
        var customTtl = TimeSpan.FromMinutes(5);

        // when
        var result = await provider.TryAcquireAsync(
            resource,
            timeUntilExpires: customTtl,
            cancellationToken: AbortToken
        );

        // then
        result.Should().NotBeNull();
        // Verify through observability
        var expiration = await provider.GetExpirationAsync(resource, AbortToken);
        expiration.Should().NotBeNull();
        expiration!.Value.Should().BeCloseTo(customTtl, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task should_use_infinite_time_until_expires()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);

        // when
        var result = await provider.TryAcquireAsync(
            resource,
            timeUntilExpires: Timeout.InfiniteTimeSpan,
            cancellationToken: AbortToken
        );

        // then
        result.Should().NotBeNull();
        // With infinite TTL, expiration should be null
        var expiration = await provider.GetExpirationAsync(resource, AbortToken);
        expiration.Should().BeNull();
    }

    [Fact]
    public async Task should_throw_when_max_waiters_exceeded()
    {
        // given
        var options = new DistributedLockOptions { MaxWaitersPerResource = 2 };
        var provider = _CreateProvider(options);
        var resource = Faker.Random.AlphaNumeric(10);

        // Pre-lock the resource
        await _storage.InsertAsync(options.KeyPrefix + resource, "existing-lock", TimeSpan.FromMinutes(5));

        var cts = new CancellationTokenSource();
        try
        {
            // Start multiple waiters - they will wait for retry
#pragma warning disable AsyncFixer04 // Intentionally not awaiting to simulate concurrent waiters
            _ = provider.TryAcquireAsync(
                resource,
                acquireTimeout: TimeSpan.FromSeconds(30),
                cancellationToken: cts.Token
            );
            await Task.Delay(100, AbortToken); // Give time for waiter1 to enter retry loop

            _ = provider.TryAcquireAsync(
                resource,
                acquireTimeout: TimeSpan.FromSeconds(30),
                cancellationToken: cts.Token
            );
            await Task.Delay(100, AbortToken); // Give time for waiter2 to enter retry loop
#pragma warning restore AsyncFixer04

            // when - third waiter should throw immediately when max exceeded
            var act = async () =>
                await provider.TryAcquireAsync(
                    resource,
                    acquireTimeout: TimeSpan.FromSeconds(30),
                    cancellationToken: cts.Token
                );

            // then
            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Maximum waiters per resource*");
        }
        finally
        {
            await cts.CancelAsync();
            cts.Dispose();
        }
    }

    [Fact]
    public async Task should_throw_when_max_concurrent_resources_exceeded()
    {
        // given
        var options = new DistributedLockOptions { MaxConcurrentWaitingResources = 2 };
        var provider = _CreateProvider(options);

        // Pre-lock different resources
        await _storage.InsertAsync(options.KeyPrefix + "resource1", "lock1", TimeSpan.FromMinutes(5));
        await _storage.InsertAsync(options.KeyPrefix + "resource2", "lock2", TimeSpan.FromMinutes(5));
        await _storage.InsertAsync(options.KeyPrefix + "resource3", "lock3", TimeSpan.FromMinutes(5));

        var cts = new CancellationTokenSource();
        try
        {
            // Start waiters on different resources
#pragma warning disable AsyncFixer04 // Intentionally not awaiting to simulate concurrent waiters
            _ = provider.TryAcquireAsync(
                "resource1",
                acquireTimeout: TimeSpan.FromSeconds(30),
                cancellationToken: cts.Token
            );
            await Task.Delay(100, AbortToken);

            _ = provider.TryAcquireAsync(
                "resource2",
                acquireTimeout: TimeSpan.FromSeconds(30),
                cancellationToken: cts.Token
            );
            await Task.Delay(100, AbortToken);
#pragma warning restore AsyncFixer04

            // when - third resource should throw
            var act = async () =>
                await provider.TryAcquireAsync(
                    "resource3",
                    acquireTimeout: TimeSpan.FromSeconds(30),
                    cancellationToken: cts.Token
                );

            // then
            await act.Should()
                .ThrowAsync<InvalidOperationException>()
                .WithMessage("*Maximum concurrent waiting resources*");
        }
        finally
        {
            await cts.CancelAsync();
            cts.Dispose();
        }
    }

    #endregion

    #region ReleaseAsync Tests

    [Fact]
    public async Task should_throw_when_release_resource_is_null()
    {
        // given
        var provider = _CreateProvider();

        // when
        var act = async () => await provider.ReleaseAsync(null!, "lock-id", AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("resource");
    }

    [Fact]
    public async Task should_throw_when_release_lock_id_is_null()
    {
        // given
        var provider = _CreateProvider();

        // when
        var act = async () => await provider.ReleaseAsync("resource", null!, AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("lockId");
    }

    [Fact]
    public async Task should_release_lock()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);

        var acquiredLock = await provider.TryAcquireAsync(resource, cancellationToken: AbortToken);
        acquiredLock.Should().NotBeNull();

        // when
        await provider.ReleaseAsync(resource, acquiredLock!.LockId, AbortToken);

        // then
        var isLocked = await provider.IsLockedAsync(resource, AbortToken);
        isLocked.Should().BeFalse();
    }

    [Fact]
    public async Task should_retry_release_on_transient_error()
    {
        // given
        var storage = Substitute.For<IDistributedLockStorage>();
        var callCount = 0;

        storage
            .RemoveIfEqualAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns(_ =>
            {
                callCount++;
                if (callCount < 3)
                {
                    throw new TimeoutException("Transient error");
                }
                return ValueTask.FromResult(true);
            });

        var provider = new DistributedLockProvider(
            storage,
            _outboxPublisher,
            new DistributedLockOptions(),
            _longIdGenerator,
            _timeProvider,
            LoggerFactory.CreateLogger<DistributedLockProvider>()
        );

        // when - run release task and advance time through backoff delays
        var releaseTask = provider.ReleaseAsync("resource", "lock-id", AbortToken);

        // Advance time to handle backoff delays
        for (var i = 0; i < 10; i++)
        {
            await Task.Yield();
            _timeProvider.Advance(TimeSpan.FromSeconds(1));
        }

        await releaseTask;

        // then
        callCount.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task should_publish_lock_released_message()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);

        var acquiredLock = await provider.TryAcquireAsync(resource, cancellationToken: AbortToken);
        acquiredLock.Should().NotBeNull();

        // when
        await provider.ReleaseAsync(resource, acquiredLock!.LockId, AbortToken);

        // then
        await _outboxPublisher
            .Received(1)
            .PublishAsync(
                Arg.Is<DistributedLockReleased>(m => m.Resource == resource && m.LockId == acquiredLock.LockId),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
    }

    #endregion

    #region RenewAsync Tests

    [Fact]
    public async Task should_throw_when_renew_resource_is_null()
    {
        // given
        var provider = _CreateProvider();

        // when
        var act = async () => await provider.RenewAsync(null!, "lock-id", cancellationToken: AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("resource");
    }

    [Fact]
    public async Task should_throw_when_renew_lock_id_is_null()
    {
        // given
        var provider = _CreateProvider();

        // when
        var act = async () => await provider.RenewAsync("resource", null!, cancellationToken: AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("lockId");
    }

    [Fact]
    public async Task should_renew_lock_if_held()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);

        var acquiredLock = await provider.TryAcquireAsync(
            resource,
            timeUntilExpires: TimeSpan.FromMinutes(5),
            cancellationToken: AbortToken
        );
        acquiredLock.Should().NotBeNull();

        // when
        var result = await provider.RenewAsync(
            resource,
            acquiredLock!.LockId,
            timeUntilExpires: TimeSpan.FromMinutes(10),
            cancellationToken: AbortToken
        );

        // then
        result.Should().BeTrue();
    }

    [Fact]
    public async Task should_return_false_if_lock_not_held()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);

        // when
        var result = await provider.RenewAsync(
            resource,
            "non-existent-lock-id",
            timeUntilExpires: TimeSpan.FromMinutes(10),
            cancellationToken: AbortToken
        );

        // then
        result.Should().BeFalse();
    }

    [Fact]
    public async Task should_extend_expiration()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);

        var acquiredLock = await provider.TryAcquireAsync(
            resource,
            timeUntilExpires: TimeSpan.FromMinutes(5),
            cancellationToken: AbortToken
        );
        acquiredLock.Should().NotBeNull();

        var expirationBefore = await provider.GetExpirationAsync(resource, AbortToken);

        // when
        await provider.RenewAsync(
            resource,
            acquiredLock!.LockId,
            timeUntilExpires: TimeSpan.FromMinutes(30),
            cancellationToken: AbortToken
        );

        // then
        var expirationAfter = await provider.GetExpirationAsync(resource, AbortToken);
        expirationAfter.Should().NotBeNull();
        expirationAfter!.Value.Should().BeGreaterThan(expirationBefore!.Value);
    }

    #endregion

    #region IsLockedAsync Tests

    [Fact]
    public async Task should_return_true_when_locked()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);
        await provider.TryAcquireAsync(resource, cancellationToken: AbortToken);

        // when
        var result = await provider.IsLockedAsync(resource, AbortToken);

        // then
        result.Should().BeTrue();
    }

    [Fact]
    public async Task should_return_false_when_not_locked()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);

        // when
        var result = await provider.IsLockedAsync(resource, AbortToken);

        // then
        result.Should().BeFalse();
    }

    #endregion

    #region Observability Tests

    [Fact]
    public async Task should_get_expiration_for_locked_resource()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);
        var ttl = TimeSpan.FromMinutes(10);
        await provider.TryAcquireAsync(resource, timeUntilExpires: ttl, cancellationToken: AbortToken);

        // when
        var result = await provider.GetExpirationAsync(resource, AbortToken);

        // then
        result.Should().NotBeNull();
        result!.Value.Should().BeCloseTo(ttl, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task should_return_null_expiration_when_not_locked()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);

        // when
        var result = await provider.GetExpirationAsync(resource, AbortToken);

        // then
        result.Should().BeNull();
    }

    [Fact]
    public async Task should_get_lock_info()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);
        var acquiredLock = await provider.TryAcquireAsync(
            resource,
            timeUntilExpires: TimeSpan.FromMinutes(5),
            cancellationToken: AbortToken
        );

        // when
        var result = await provider.GetLockInfoAsync(resource, AbortToken);

        // then
        result.Should().NotBeNull();
        result!.Resource.Should().Be(resource);
        result.LockId.Should().Be(acquiredLock!.LockId);
        result.TimeToLive.Should().NotBeNull();
    }

    [Fact]
    public async Task should_return_null_lock_info_when_not_locked()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);

        // when
        var result = await provider.GetLockInfoAsync(resource, AbortToken);

        // then
        result.Should().BeNull();
    }

    [Fact]
    public async Task should_list_active_locks()
    {
        // given
        var provider = _CreateProvider();
        var resources = Enumerable.Range(0, 3).Select(_ => Faker.Random.AlphaNumeric(10)).ToList();

        foreach (var resource in resources)
        {
            await provider.TryAcquireAsync(resource, cancellationToken: AbortToken);
        }

        // when
        var result = await provider.ListActiveLocksAsync(AbortToken);

        // then
        result.Should().HaveCount(3);
        result.Select(l => l.Resource).Should().BeEquivalentTo(resources);
    }

    [Fact]
    public async Task should_get_active_locks_count()
    {
        // given
        var provider = _CreateProvider();
        for (var i = 0; i < 5; i++)
        {
            var resource = Faker.Random.AlphaNumeric(10);
            await provider.TryAcquireAsync(resource, cancellationToken: AbortToken);
        }

        // when
        var result = await provider.GetActiveLocksCountAsync(AbortToken);

        // then
        result.Should().Be(5);
    }

    [Fact]
    public async Task should_return_zero_active_locks_count_when_empty()
    {
        // given
        var provider = _CreateProvider();

        // when
        var result = await provider.GetActiveLocksCountAsync(AbortToken);

        // then
        result.Should().Be(0);
    }

    [Fact]
    public async Task should_return_empty_list_when_no_active_locks()
    {
        // given
        var provider = _CreateProvider();

        // when
        var result = await provider.ListActiveLocksAsync(AbortToken);

        // then
        result.Should().BeEmpty();
    }

    #endregion
}
