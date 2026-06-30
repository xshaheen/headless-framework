// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class HybridCacheDistributedResilienceTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();

    [Fact]
    public async Task should_apply_l2_soft_timeout_and_serve_l1_stale_without_running_factory()
    {
        // given
        var timeProvider = TimeProvider.System;
        var softTimeout = TimeSpan.FromMilliseconds(50);
        var l1 = new InMemoryCache(timeProvider, new InMemoryCacheOptions { CloneValues = true });
        using var l2 = new GatedRemoteCache(timeProvider)
        {
            ReadGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        var cache = _CreateCache(
            l1,
            l2,
            new HybridCacheOptions
            {
                DistributedCacheSoftTimeout = softTimeout,
                DistributedCacheHardTimeout = TimeSpan.FromSeconds(5),
            },
            timeProvider
        );
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        var staleValue = Faker.Random.Int(1, 100);
        await _PlantStaleEntryAsync(l1, key, staleValue, timeProvider);

        var factoryCalls = 0;
        var task = cache
            .GetOrAddAsync(
                key,
                _ =>
                {
                    factoryCalls++;
                    return new ValueTask<int>(999);
                },
                _FailSafeOptions(),
                AbortToken
            )
            .AsTask();

        // when
        await l2.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        var result = await task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // then
        result.HasValue.Should().BeTrue();
        result.Value.Should().Be(staleValue);
        result.IsStale.Should().BeTrue();
        factoryCalls.Should().Be(0, "a soft L2 timeout with an L1 reserve should not run the origin factory");
        l2.ReadAttempts.Should().Be(1);
    }

    [Fact]
    public async Task should_apply_l2_hard_timeout_when_no_l1_fallback_exists()
    {
        // given
        var timeProvider = TimeProvider.System;
        var softTimeout = TimeSpan.FromMilliseconds(50);
        var hardTimeout = TimeSpan.FromMilliseconds(150);
        var l1 = new InMemoryCache(timeProvider, new InMemoryCacheOptions { CloneValues = true });
        using var l2 = new GatedRemoteCache(timeProvider)
        {
            ReadGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        var cache = _CreateCache(
            l1,
            l2,
            new HybridCacheOptions
            {
                DistributedCacheSoftTimeout = softTimeout,
                DistributedCacheHardTimeout = hardTimeout,
            },
            timeProvider
        );
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        var factoryCalls = 0;
        var task = cache
            .GetOrAddAsync<int>(
                key,
                _ =>
                {
                    factoryCalls++;
                    throw new InvalidOperationException("origin unavailable");
                },
                _FailSafeOptions(),
                AbortToken
            )
            .AsTask();

        await l2.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // when — no L1 reserve exists, so the soft timeout must not be selected for the factory-store read
        await Task.Delay(softTimeout + TimeSpan.FromMilliseconds(25), AbortToken);

        // then
        task.IsCompleted.Should().BeFalse("without a local fallback the L2 read should wait for the hard timeout");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken)
        );
        exception.Message.Should().Be("origin unavailable");
        factoryCalls.Should().Be(1);
        l2.ReadAttempts.Should()
            .Be(2, "the coordinator performs the normal under-lock re-check before running the factory");
    }

    [Fact]
    public async Task should_return_miss_from_plain_get_when_l2_soft_timeout_elapses()
    {
        // given
        var timeProvider = TimeProvider.System;
        var softTimeout = TimeSpan.FromMilliseconds(50);
        var l1 = new InMemoryCache(timeProvider, new InMemoryCacheOptions { CloneValues = true });
        using var l2 = new GatedRemoteCache(timeProvider)
        {
            ReadGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        var cache = _CreateCache(
            l1,
            l2,
            new HybridCacheOptions
            {
                DistributedCacheSoftTimeout = softTimeout,
                DistributedCacheHardTimeout = TimeSpan.FromSeconds(5),
            },
            timeProvider
        );
        await using var _ = cache;

        var task = cache.GetAsync<int>(Faker.Random.AlphaNumeric(10), AbortToken).AsTask();

        // when
        await l2.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        var result = await task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // then
        result.HasValue.Should().BeFalse();
        l2.ReadAttempts.Should().Be(1);
    }

    [Fact]
    public async Task should_skip_l2_reads_while_distributed_cache_circuit_is_open_then_probe_after_duration()
    {
        // given
        var circuitDuration = TimeSpan.FromSeconds(5);
        var l1 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions { CloneValues = true });
        using var l2 = new TogglableRemoteCache(_timeProvider);
        var cache = _CreateCache(
            l1,
            l2,
            new HybridCacheOptions { DistributedCacheCircuitBreakerDuration = circuitDuration }
        );
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        await l2.UpsertAsync(key, 42, TimeSpan.FromMinutes(5), AbortToken);

        // when — the first L2 read fails and opens the circuit
        l2.FailReads = true;
        var failedRead = await cache.GetAsync<int>(key, AbortToken);

        // then
        failedRead.HasValue.Should().BeFalse();
        l2.ReadAttempts.Should().Be(1);

        // when — L2 recovers but the circuit is still open
        l2.FailReads = false;
        var skippedRead = await cache.GetAsync<int>(key, AbortToken);

        // then — no second L2 call is made while the circuit is open
        skippedRead.HasValue.Should().BeFalse();
        l2.ReadAttempts.Should().Be(1);

        // when — the breaker duration elapses
        _timeProvider.Advance(circuitDuration + TimeSpan.FromTicks(1));
        var recoveredRead = await cache.GetAsync<int>(key, AbortToken);

        // then — Hybrid probes L2 again and promotes the recovered value
        recoveredRead.Value.Should().Be(42);
        l2.ReadAttempts.Should().Be(2, "value + expiration are now fused into a single GetWithExpirationAsync call");
        (await l1.GetAsync<int>(key, AbortToken)).Value.Should().Be(42);
    }

    [Fact]
    public async Task should_keep_l1_tag_invalidation_and_trip_circuit_when_l2_marker_bump_fails()
    {
        // given — an entry tagged and present in both tiers
        var l1 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions { CloneValues = true });
        using var l2 = new TogglableRemoteCache(_timeProvider);
        var cache = _CreateCache(
            l1,
            l2,
            new HybridCacheOptions { DistributedCacheCircuitBreakerDuration = TimeSpan.FromSeconds(5) }
        );
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        var tag = Faker.Random.AlphaNumeric(8);
        await cache.UpsertEntryAsync(
            key,
            42,
            new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5), Tags = [tag] },
            AbortToken
        );

        // when — the L2 marker bump throws (advance so the marker postdates the write)
        _timeProvider.Advance(TimeSpan.FromMilliseconds(10));
        l2.FailMarkerBumps = true;
        var act = async () => await cache.RemoveByTagAsync(tag, AbortToken);

        // then — the call is best-effort (does not throw), and the L1 marker was bumped first so this node's own
        // read already misses even though the L2 bump failed (the #1 L1-first guarantee)
        await act.Should().NotThrowAsync();
        (await l1.GetAsync<int>(key, AbortToken)).HasValue.Should().BeFalse();

        // and the failed L2 bump tripped the distributed-cache circuit: a subsequent L2 read is skipped
        l2.FailMarkerBumps = false;
        var other = Faker.Random.AlphaNumeric(10);
        await l2.UpsertAsync(other, 7, TimeSpan.FromMinutes(5), AbortToken);
        (await cache.GetAsync<int>(other, AbortToken))
            .HasValue.Should()
            .BeFalse("the circuit opened when the L2 marker bump failed, so L2 reads are skipped until it closes");
        l2.ReadAttempts.Should().Be(0);
    }

    [Fact]
    public async Task should_rethrow_l2_read_exception_on_direct_get_when_enabled()
    {
        // given
        var timeProvider = TimeProvider.System;
        var l1 = new InMemoryCache(timeProvider, new InMemoryCacheOptions { CloneValues = true });
        using var l2 = new GatedRemoteCache(timeProvider) { ReadFault = new InvalidOperationException("l2 read down") };
        var cache = _CreateCache(
            l1,
            l2,
            new HybridCacheOptions { ReThrowDistributedCacheExceptions = true },
            timeProvider
        );
        await using var _ = cache;

        // when
        var act = async () => await cache.GetAsync<int>(Faker.Random.AlphaNumeric(10), AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("l2 read down");
    }

    [Fact]
    public async Task should_swallow_l2_read_exception_on_direct_get_by_default()
    {
        // given
        var timeProvider = TimeProvider.System;
        using var l1 = new InMemoryCache(timeProvider, new InMemoryCacheOptions { CloneValues = true });
        using var l2 = new GatedRemoteCache(timeProvider) { ReadFault = new InvalidOperationException("l2 read down") };
        var cache = _CreateCache(l1, l2, new HybridCacheOptions(), timeProvider);
        await using var _ = cache;

        // when — default policy degrades an L2 read fault to a miss
        var result = await cache.GetAsync<int>(Faker.Random.AlphaNumeric(10), AbortToken);

        // then
        result.HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task should_rethrow_l2_write_exception_on_factory_write_when_enabled()
    {
        // given
        var timeProvider = TimeProvider.System;
        var l1 = new InMemoryCache(timeProvider, new InMemoryCacheOptions { CloneValues = true });
        using var l2 = new GatedRemoteCache(timeProvider)
        {
            WriteFault = new InvalidOperationException("l2 write down"),
        };
        var cache = _CreateCache(
            l1,
            l2,
            new HybridCacheOptions { ReThrowDistributedCacheExceptions = true },
            timeProvider
        );
        await using var _ = cache;

        // when — the factory produces a value but the L2 store-write faults
        var act = async () =>
            await cache.GetOrAddAsync(
                Faker.Random.AlphaNumeric(10),
                _ => new ValueTask<int>(42),
                TimeSpan.FromMinutes(5),
                AbortToken
            );

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("l2 write down");
    }

    [Fact]
    public async Task should_rethrow_backplane_exception_on_write_when_enabled()
    {
        // given
        var timeProvider = TimeProvider.System;
        var l1 = new InMemoryCache(timeProvider, new InMemoryCacheOptions { CloneValues = true });
        using var l2 = new GatedRemoteCache(timeProvider);
        var cache = new HybridCache(
            l1,
            l2,
            _FaultingPublisher("backplane down"),
            new HybridCacheOptions { ReThrowBackplaneExceptions = true },
            timeProvider: timeProvider
        );
        await using var _ = cache;

        // when
        var act = async () =>
            await cache.UpsertAsync(Faker.Random.AlphaNumeric(10), 7, TimeSpan.FromMinutes(5), AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("backplane down");
    }

    [Fact]
    public async Task should_swallow_backplane_exception_on_write_by_default()
    {
        // given
        var timeProvider = TimeProvider.System;
        var l1 = new InMemoryCache(timeProvider, new InMemoryCacheOptions { CloneValues = true });
        using var l2 = new GatedRemoteCache(timeProvider);
        var cache = new HybridCache(
            l1,
            l2,
            _FaultingPublisher("backplane down"),
            new HybridCacheOptions(),
            timeProvider: timeProvider
        );
        await using var _ = cache;

        // when — default policy keeps a publish failure non-fatal (eventual consistency)
        var act = async () =>
            await cache.UpsertAsync(Faker.Random.AlphaNumeric(10), 7, TimeSpan.FromMinutes(5), AbortToken);

        // then
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task should_rearm_l1_and_open_circuit_when_l2_refresh_throws()
    {
        // given — a sliding entry in both tiers; L1's local ceiling is wide enough to re-arm within
        var localExpiration = TimeSpan.FromSeconds(1);
        var duration = TimeSpan.FromSeconds(2);
        var slidingExpiration = TimeSpan.FromMilliseconds(400);
        var l1 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions { CloneValues = true });
        using var l2 = new TogglableRemoteCache(_timeProvider);
        var cache = _CreateCache(
            l1,
            l2,
            new HybridCacheOptions
            {
                DefaultLocalExpiration = localExpiration,
                DistributedCacheCircuitBreakerDuration = TimeSpan.FromSeconds(5),
            }
        );
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        await cache.UpsertEntryAsync(
            key,
            Faker.Random.Int(),
            new CacheEntryOptions { Duration = duration, SlidingExpiration = slidingExpiration },
            AbortToken
        );

        // advance past the half-window throttle so the re-arm fires, then capture the re-arm instant
        _timeProvider.Advance(TimeSpan.FromMilliseconds(300));
        var rearmedAt = _timeProvider.GetUtcNow().UtcDateTime;

        // when — the L2 sliding re-arm throws
        l2.FailRefresh = true;
        var act = async () => await cache.RefreshAsync(key, AbortToken);

        // then — the refresh is best-effort (does not throw) and L1 was still re-armed despite the L2 failure
        await act.Should().NotThrowAsync();

        var l1Entry = await ((IFactoryCacheStore)l1).TryGetEntryAsync<int>(key, AbortToken);
        l1Entry.Found.Should().BeTrue();
        l1Entry
            .LogicalExpiresAt.Should()
            .Be(rearmedAt.Add(slidingExpiration), "L1 must re-arm even when the L2 refresh fails");

        // and the failed L2 refresh tripped the distributed-cache circuit: a subsequent L2 read is skipped
        l2.FailRefresh = false;
        var other = Faker.Random.AlphaNumeric(10);
        await l2.UpsertAsync(other, 7, TimeSpan.FromMinutes(5), AbortToken);
        var attemptsBeforeSkippedRead = l2.ReadAttempts;

        var skipped = await cache.GetAsync<int>(other, AbortToken);

        skipped.HasValue.Should().BeFalse("the circuit opened on the L2 refresh failure, so L2 reads are skipped");
        l2.ReadAttempts.Should().Be(attemptsBeforeSkippedRead, "an open circuit must not issue the L2 read");
    }

    private static IBus _FaultingPublisher(string message)
    {
        var publisher = Substitute.For<IBus>();
        publisher
            .PublishAsync(Arg.Any<CacheInvalidationMessage>(), Arg.Any<PublishOptions?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new InvalidOperationException(message)));

        return publisher;
    }

    private HybridCache _CreateCache(
        IInMemoryCache l1,
        IRemoteCache l2,
        HybridCacheOptions options,
        TimeProvider? timeProvider = null
    )
    {
        var publisher = Substitute.For<IBus>();
        publisher
            .PublishAsync(Arg.Any<CacheInvalidationMessage>(), Arg.Any<PublishOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        return new HybridCache(l1, l2, publisher, options, timeProvider: timeProvider ?? _timeProvider);
    }

    private async ValueTask _PlantStaleEntryAsync<T>(
        IInMemoryCache cache,
        string key,
        T value,
        TimeProvider timeProvider
    )
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        await ((IFactoryCacheStore)cache).SetEntryAsync(
            key,
            new CacheStoreEntryWrite<T>
            {
                Value = value,
                IsNull = false,
                LogicalExpiresAt = now.AddMinutes(-1),
                PhysicalExpiresAt = now.AddHours(1),
            },
            AbortToken
        );
    }

    private static CacheEntryOptions _FailSafeOptions() =>
        new()
        {
            Duration = TimeSpan.FromMinutes(1),
            IsFailSafeEnabled = true,
            FailSafeMaxDuration = TimeSpan.FromHours(1),
            FailSafeThrottleDuration = TimeSpan.FromSeconds(30),
        };
}
