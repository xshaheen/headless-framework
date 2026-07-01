// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class HybridCacheFailSafeTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();

    // The HybridCache returned here is disposed per test via `await using`, but it does not own the injected
    // L1/L2 stores. This fixture collects those raw InMemoryCache instances and disposes them at teardown.
    private readonly List<object> _disposables = [];

    private (HybridCache cache, IInMemoryCache l1, IRemoteCache l2, IBus publisher) _CreateCache(
        HybridCacheOptions? options = null
    )
    {
        options ??= new HybridCacheOptions();
        var l1Options = new InMemoryCacheOptions { CloneValues = true };
        var l1 = new InMemoryCache(_timeProvider, l1Options);

        var l2Options = new InMemoryCacheOptions { CloneValues = true };
        var l2Inner = new InMemoryCache(_timeProvider, l2Options);
        var l2 = new InMemoryRemoteCacheAdapter(l2Inner);

        var publisher = Substitute.For<IBus>();
        publisher
            .PublishAsync(Arg.Any<CacheInvalidationMessage>(), Arg.Any<PublishOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var cache = new HybridCache(l1, l2, publisher, options, timeProvider: _timeProvider);

        _disposables.Add(l1);
        _disposables.Add(l2Inner);

        return (cache, l1, l2, publisher);
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        foreach (var disposable in _disposables)
        {
            switch (disposable)
            {
                case IAsyncDisposable asyncDisposable:
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                    break;
                case IDisposable syncDisposable:
                    syncDisposable.Dispose();
                    break;
            }
        }

        _disposables.Clear();
        await base.DisposeAsyncCore().ConfigureAwait(false);
    }

    // Returns CacheEntryOptions with fail-safe enabled using sensible defaults for tests.
    private static CacheEntryOptions _FailSafeOptions(
        TimeSpan? duration = null,
        TimeSpan? failSafeMaxDuration = null,
        TimeSpan? throttleDuration = null
    ) =>
        new()
        {
            Duration = duration ?? TimeSpan.FromMinutes(5),
            IsFailSafeEnabled = true,
            FailSafeMaxDuration = failSafeMaxDuration ?? TimeSpan.FromHours(1),
            FailSafeThrottleDuration = throttleDuration ?? TimeSpan.FromSeconds(30),
        };

    #region U7-1: stale from L1 when factory throws

    [Fact]
    public async Task should_serve_stale_from_l1_when_factory_throws_and_failsafe_enabled()
    {
        // given
        var (cache, l1, _, _) = _CreateCache();
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        var staleValue = Faker.Random.Int(1, 100);
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // Plant a logically-expired (stale) but physically-present entry directly into L1.
        var logicallyExpiredAt = now.AddMinutes(-1); // already stale
        var physicallyExpiredAt = now.AddHours(1); // still physically held
        await ((IFactoryCacheStore)l1).SetEntryAsync(
            key,
            new CacheStoreEntryWrite<int>
            {
                Value = staleValue,
                IsNull = false,
                LogicalExpiresAt = logicallyExpiredAt,
                PhysicalExpiresAt = physicallyExpiredAt,
            },
            AbortToken
        );

        var opts = _FailSafeOptions();

        // when — factory throws
        var result = await cache.GetOrAddAsync<int>(
            key,
            _ => throw new InvalidOperationException("upstream unavailable"),
            opts,
            AbortToken
        );

        // then — stale reserve served
        result.HasValue.Should().BeTrue();
        result.Value.Should().Be(staleValue);
        result.IsStale.Should().BeTrue("fail-safe must mark the value as stale");
    }

    #endregion

    #region U7-2: stale from L2 when L1 empty and factory throws

    [Fact]
    public async Task should_serve_stale_from_l2_when_l1_empty_and_factory_throws()
    {
        // given
        var (cache, _, l2, _) = _CreateCache();
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        var staleValue = Faker.Random.Int(1, 100);
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // Plant a logically-expired but physically-present entry directly into L2 via IFactoryCacheStore.
        var logicallyExpiredAt = now.AddMinutes(-1);
        var physicallyExpiredAt = now.AddHours(1);
        await ((IFactoryCacheStore)l2).SetEntryAsync(
            key,
            new CacheStoreEntryWrite<int>
            {
                Value = staleValue,
                IsNull = false,
                LogicalExpiresAt = logicallyExpiredAt,
                PhysicalExpiresAt = physicallyExpiredAt,
            },
            AbortToken
        );

        var opts = _FailSafeOptions();

        // when — factory throws; L1 is empty so the coordinator must find the reserve in L2
        var result = await cache.GetOrAddAsync<int>(
            key,
            _ => throw new InvalidOperationException("upstream unavailable"),
            opts,
            AbortToken
        );

        // then — stale reserve sourced from L2 is served
        result.HasValue.Should().BeTrue();
        result.Value.Should().Be(staleValue);
        result.IsStale.Should().BeTrue("fail-safe must mark the value as stale when sourced from L2");
    }

    #endregion

    #region U7-3: stale from L1 when L2 read throws and factory throws

    [Fact]
    public async Task should_serve_stale_from_l1_when_l2_read_throws_and_factory_throws()
    {
        // given
        var l1Options = new InMemoryCacheOptions { CloneValues = true };
        using var l1Cache = new InMemoryCache(_timeProvider, l1Options);

        // L2 is a store whose read throws to simulate a down Redis node.
        using var l2 = new ThrowingReadRemoteCache(_timeProvider);

        var publisher = Substitute.For<IBus>();
        publisher
            .PublishAsync(Arg.Any<CacheInvalidationMessage>(), Arg.Any<PublishOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var cache = new HybridCache(l1Cache, l2, publisher, new HybridCacheOptions(), timeProvider: _timeProvider);
        await using var __ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        var staleValue = Faker.Random.Int(1, 100);
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // Plant a logically-expired but physically-present stale entry into L1.
        var logicallyExpiredAt = now.AddMinutes(-1);
        var physicallyExpiredAt = now.AddHours(1);
        await ((IFactoryCacheStore)l1Cache).SetEntryAsync(
            key,
            new CacheStoreEntryWrite<int>
            {
                Value = staleValue,
                IsNull = false,
                LogicalExpiresAt = logicallyExpiredAt,
                PhysicalExpiresAt = physicallyExpiredAt,
            },
            AbortToken
        );

        var opts = _FailSafeOptions();

        // when — L2 read throws, factory throws → should fall back to L1 stale reserve
        var result = await cache.GetOrAddAsync<int>(
            key,
            _ => throw new InvalidOperationException("upstream unavailable"),
            opts,
            AbortToken
        );

        // then — L1 stale served despite L2 read failure
        result.HasValue.Should().BeTrue();
        result.Value.Should().Be(staleValue);
        result.IsStale.Should().BeTrue("L1 stale reserve must be served when L2 read fails and factory throws");
    }

    [Fact]
    public async Task should_serve_stale_from_l1_when_l2_does_not_support_factory_store()
    {
        // given
        var l1Options = new InMemoryCacheOptions { CloneValues = true };
        using var l1Cache = new InMemoryCache(_timeProvider, l1Options);
        var l2 = Substitute.For<IRemoteCache>();
        l2.GetAsync<int>(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(CacheValue<int>.NoValue);
        l2.GetExpirationAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((TimeSpan?)null);

        var publisher = Substitute.For<IBus>();
        publisher
            .PublishAsync(Arg.Any<CacheInvalidationMessage>(), Arg.Any<PublishOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var cache = new HybridCache(l1Cache, l2, publisher, new HybridCacheOptions(), timeProvider: _timeProvider);
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        var staleValue = Faker.Random.Int(1, 100);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        await ((IFactoryCacheStore)l1Cache).SetEntryAsync(
            key,
            new CacheStoreEntryWrite<int>
            {
                Value = staleValue,
                IsNull = false,
                LogicalExpiresAt = now.AddMinutes(-1),
                PhysicalExpiresAt = now.AddHours(1),
            },
            AbortToken
        );

        // when
        var result = await cache.GetOrAddAsync<int>(
            key,
            _ => throw new InvalidOperationException("upstream unavailable"),
            _FailSafeOptions(),
            AbortToken
        );

        // then
        result.Value.Should().Be(staleValue);
        result.IsStale.Should().BeTrue();
    }

    #endregion

    #region U7-4: no behavior change when fail-safe disabled

    [Fact]
    public async Task should_preserve_two_tier_behavior_when_failsafe_disabled()
    {
        // given
        var (cache, l1, l2, _) = _CreateCache(new HybridCacheOptions { DefaultLocalExpiration = null });
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        var value = Faker.Random.Int(1, 1000);
        var factoryCallCount = 0;

        var duration = TimeSpan.FromMinutes(10);
        var opts = new CacheEntryOptions { Duration = duration }; // fail-safe disabled by default

        // Phase 1: cold miss → factory called, both caches populated
        var result1 = await cache.GetOrAddAsync(
            key,
            _ =>
            {
                factoryCallCount++;
                return new ValueTask<int?>(value);
            },
            opts,
            AbortToken
        );

        result1.HasValue.Should().BeTrue();
        result1.Value.Should().Be(value);
        result1.IsStale.Should().BeFalse();
        factoryCallCount.Should().Be(1, "factory called once on cold miss");

        // Phase 2: L1 hit — factory NOT called
        var result2 = await cache.GetOrAddAsync(
            key,
            _ =>
            {
                factoryCallCount++;
                return new ValueTask<int?>(999);
            },
            opts,
            AbortToken
        );

        result2.HasValue.Should().BeTrue();
        result2.Value.Should().Be(value);
        result2.IsStale.Should().BeFalse();
        factoryCallCount.Should().Be(1, "factory must NOT be called on L1 hit");

        // Phase 3: advance time past Duration → logical and physical expiry both pass → factory re-runs
        _timeProvider.Advance(duration.Add(TimeSpan.FromSeconds(1)));

        var result3 = await cache.GetOrAddAsync(
            key,
            _ =>
            {
                factoryCallCount++;
                return new ValueTask<int?>(value);
            },
            opts,
            AbortToken
        );

        result3.HasValue.Should().BeTrue();
        result3.IsStale.Should().BeFalse("fail-safe is off; expired entries must not be served stale");
        factoryCallCount.Should().Be(2, "factory must re-run after full expiry with fail-safe disabled");
    }

    #endregion

    #region U7-5: factory success writes L2 physical + bounded L1

    [Fact]
    public async Task should_write_l2_physical_and_l1_local_expiration_on_factory_success()
    {
        // given
        var localCap = TimeSpan.FromMinutes(2);
        var duration = TimeSpan.FromMinutes(10);
        var (cache, l1, l2, publisher) = _CreateCache(new HybridCacheOptions { DefaultLocalExpiration = localCap });
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        var value = Faker.Random.Int(1, 1000);

        var opts = _FailSafeOptions(duration: duration, failSafeMaxDuration: TimeSpan.FromHours(2));

        // when — factory succeeds
        var result = await cache.GetOrAddAsync(key, _ => new ValueTask<int?>(value), opts, AbortToken);

        // then — value returned fresh (not stale)
        result.HasValue.Should().BeTrue();
        result.Value.Should().Be(value);
        result.IsStale.Should().BeFalse();

        // L2 physical expiration = max(duration, failSafeMaxDuration) = 2 h.
        // GetExpirationAsync returns the *logical* TTL, so we must read the raw entry to inspect PhysicalExpiresAt.
        var l2Entry = await ((IFactoryCacheStore)l2).TryGetEntryAsync<int>(key, AbortToken);
        l2Entry.Found.Should().BeTrue("coordinator must have written the entry to L2");
        l2Entry.PhysicalExpiresAt.Should().NotBeNull("fail-safe writes must include physical expiry");
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        (l2Entry.PhysicalExpiresAt!.Value - now)
            .Should()
            .BeGreaterThan(duration, "L2 must hold the fail-safe physical reserve beyond logical TTL");

        // L1 expiration must be bounded by DefaultLocalExpiration
        var l1Exp = await l1.GetExpirationAsync(key, AbortToken);
        l1Exp.Should().HaveValue();
        l1Exp!
            .Value.Should()
            .BeLessThanOrEqualTo(
                localCap.Add(TimeSpan.FromSeconds(1)),
                "L1 expiration must be capped by DefaultLocalExpiration"
            );

        // The factory value-write broadcasts a key invalidation so peers drop their stale L1 copies
        await publisher
            .Received(1)
            .PublishAsync(
                Arg.Is<CacheInvalidationMessage>(m => m.Key == key),
                Arg.Any<PublishOptions?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_cap_get_all_l1_promotion_by_l2_logical_expiration()
    {
        // given
        var localCap = TimeSpan.FromHours(1);
        var duration = TimeSpan.FromMinutes(5);
        var (cache, l1, l2, _) = _CreateCache(new HybridCacheOptions { DefaultLocalExpiration = localCap });
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        var value = Faker.Random.Int(1, 100);
        var options = _FailSafeOptions(duration: duration, failSafeMaxDuration: TimeSpan.FromHours(2));

        await l2.GetOrAddAsync(key, _ => ValueTask.FromResult<int?>(value), options, AbortToken);

        // when — GetAllAsync reads the fresh L2 value and promotes it into L1.
        var values = await cache.GetAllAsync<int>([key], AbortToken);
        var l1Expiration = await l1.GetExpirationAsync(key, AbortToken);

        _timeProvider.Advance(duration + TimeSpan.FromMilliseconds(1));
        var afterLogicalExpiry = await cache.GetAsync<int>(key, AbortToken);

        // then — the promoted L1 copy must expire at the L2 logical boundary, not the longer local cap.
        values[key].Value.Should().Be(value);
        l1Expiration.Should().NotBeNull();
        l1Expiration.Should().BeLessThanOrEqualTo(duration, "batch promotion must not outlive L2 logical freshness");
        afterLogicalExpiry
            .HasValue.Should()
            .BeFalse("normal Hybrid reads must not serve fail-safe reserves after logical expiry");
    }

    #endregion

    #region U7-6: read-path guard — logically-expired L2 entry must NOT be promoted into L1

    [Fact]
    public async Task should_not_promote_logically_expired_l2_into_l1_on_read()
    {
        // given — only L2 holds a logically-expired (physically-present) reserve; L1 is empty
        var (cache, l1, l2, _) = _CreateCache(new HybridCacheOptions { DefaultLocalExpiration = null });
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        var staleValue = Faker.Random.Int(1, 100);
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var logicallyExpiredAt = now.AddMinutes(-1);
        var physicallyExpiredAt = now.AddHours(1);
        await ((IFactoryCacheStore)l2).SetEntryAsync(
            key,
            new CacheStoreEntryWrite<int>
            {
                Value = staleValue,
                IsNull = false,
                LogicalExpiresAt = logicallyExpiredAt,
                PhysicalExpiresAt = physicallyExpiredAt,
            },
            AbortToken
        );

        // when — drive the composite read primitive directly (no factory / activation / restamp involved)
        var entry = await ((IFactoryCacheStore)cache).TryGetEntryAsync<int>(key, AbortToken);

        // then — the entry is surfaced so the coordinator can use it as a stale candidate
        entry.Found.Should().BeTrue("the composite read must surface the L2 reserve as a stale candidate");
        entry.Value.Should().Be(staleValue);

        // and — the read path must NOT have promoted the logically-expired reserve into L1
        var l1Entry = await ((IFactoryCacheStore)l1).TryGetEntryAsync<int>(key, AbortToken);
        l1Entry
            .Found.Should()
            .BeFalse(
                "the read-path guard must not promote a logically-expired L2 reserve into L1 (#9): "
                    + "promoting on every fail-safe read amplifies L1 writes and can overwrite a newer L1 reserve"
            );
    }

    [Fact]
    public async Task should_cap_public_get_l1_promotion_by_l2_logical_expiration()
    {
        // given
        var localCap = TimeSpan.FromMinutes(5);
        var logicalTtl = TimeSpan.FromSeconds(30);
        var (cache, l1, l2, _) = _CreateCache(new HybridCacheOptions { DefaultLocalExpiration = localCap });
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        var value = Faker.Random.Int(1, 100);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        await ((IFactoryCacheStore)l2).SetEntryAsync(
            key,
            new CacheStoreEntryWrite<int>
            {
                Value = value,
                IsNull = false,
                LogicalExpiresAt = now.Add(logicalTtl),
                PhysicalExpiresAt = now.AddHours(1),
            },
            AbortToken
        );

        // when
        var result = await cache.GetAsync<int>(key, AbortToken);

        // then
        result.Value.Should().Be(value);
        var l1Expiration = await l1.GetExpirationAsync(key, AbortToken);
        l1Expiration.Should().HaveValue();
        l1Expiration!.Value.Should().BeLessThan(localCap);
        l1Expiration.Value.Should().BeCloseTo(logicalTtl, TimeSpan.FromSeconds(2));

        _timeProvider.Advance(logicalTtl + TimeSpan.FromSeconds(1));

        var afterLogicalExpiry = await cache.GetAsync<int>(key, AbortToken);
        afterLogicalExpiry.HasValue.Should().BeFalse();
    }

    #endregion

    #region U7-7: fail-safe activation refreshes L1 with a throttled, logically-fresh entry (FusionCache parity)

    [Fact]
    public async Task should_refresh_l1_with_throttled_entry_when_failsafe_activates()
    {
        // given — only L2 holds the reserve; L1 is empty
        var throttle = TimeSpan.FromSeconds(30);
        var (cache, l1, l2, _) = _CreateCache(new HybridCacheOptions { DefaultLocalExpiration = null });
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        var staleValue = Faker.Random.Int(1, 100);
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var logicallyExpiredAt = now.AddMinutes(-1);
        var physicallyExpiredAt = now.AddHours(1);
        await ((IFactoryCacheStore)l2).SetEntryAsync(
            key,
            new CacheStoreEntryWrite<int>
            {
                Value = staleValue,
                IsNull = false,
                LogicalExpiresAt = logicallyExpiredAt,
                PhysicalExpiresAt = physicallyExpiredAt,
            },
            AbortToken
        );

        var opts = _FailSafeOptions(throttleDuration: throttle);
        var factoryCallCount = 0;

        // when — factory throws → fail-safe activates from the L2-only reserve
        var activation = await cache.GetOrAddAsync<int>(
            key,
            _ =>
            {
                factoryCallCount++;
                throw new InvalidOperationException("upstream unavailable");
            },
            opts,
            AbortToken
        );

        // then — the activating call returns the stale value
        activation.HasValue.Should().BeTrue();
        activation.Value.Should().Be(staleValue);
        activation.IsStale.Should().BeTrue("fail-safe activation serves the reserve as stale");
        factoryCallCount.Should().Be(1, "factory ran once on the activating call");

        // Direct L1 assertion: after fail-safe, the throttle entry must be present in L1 with
        // LogicalExpiresAt ≈ now + FailSafeThrottleDuration (in the future, within physical reserve).
        // This assertion fails if the L1 restamp write is removed.
        var l1Entry = await ((IFactoryCacheStore)l1).TryGetEntryAsync<int>(key, AbortToken);
        l1Entry.Found.Should().BeTrue("fail-safe must write a throttle entry into L1 after activation");
        l1Entry
            .LogicalExpiresAt.Should()
            .NotBeNull("throttle entry must carry a logical expiration so future reads see it as fresh");
        l1Entry
            .LogicalExpiresAt!.Value.Should()
            .BeAfter(now, "throttle logical expiry must be in the future (now + FailSafeThrottleDuration)");
        l1Entry
            .PhysicalExpiresAt.Should()
            .NotBeNull("throttle entry must carry a physical expiration so it can eventually be evicted");
        l1Entry
            .PhysicalExpiresAt!.Value.Should()
            .BeOnOrAfter(l1Entry.LogicalExpiresAt.Value, "physical expiry must be at or after logical expiry");

        // and — fail-safe refreshed L1 with a logically-fresh throttle entry (FusionCache parity, KTD-4):
        // a subsequent read within the throttle window is a normal L1 hit — fresh, factory not invoked
        var second = await cache.GetOrAddAsync<int>(
            key,
            _ =>
            {
                factoryCallCount++;
                throw new InvalidOperationException("upstream STILL unavailable");
            },
            opts,
            AbortToken
        );

        second.HasValue.Should().BeTrue();
        second.Value.Should().Be(staleValue);
        second.IsStale.Should().BeFalse("the throttle entry is logically fresh, so the read is a normal L1 hit");
        factoryCallCount.Should().Be(1, "the throttle window must absorb the read without re-invoking the factory");
    }

    [Fact]
    public async Task should_not_publish_when_failsafe_throttle_restamps_stale_entry()
    {
        // given — a logically-expired, physically-present reserve in L1
        var (cache, l1, _, publisher) = _CreateCache();
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        var staleValue = Faker.Random.Int(1, 100);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        await ((IFactoryCacheStore)l1).SetEntryAsync(
            key,
            new CacheStoreEntryWrite<int>
            {
                Value = staleValue,
                IsNull = false,
                LogicalExpiresAt = now.AddMinutes(-1),
                PhysicalExpiresAt = now.AddHours(1),
            },
            AbortToken
        );

        // when — fail-safe activates and the throttle restamp is written through the composite store
        var result = await cache.GetOrAddAsync<int>(
            key,
            _ => throw new InvalidOperationException("upstream unavailable"),
            _FailSafeOptions(),
            AbortToken
        );

        // then — the restamp does not change the cached bytes, so no invalidation is broadcast: peers keep
        // serving their (byte-identical) copies instead of being forced into pointless L2 re-reads
        result.Value.Should().Be(staleValue);
        result.IsStale.Should().BeTrue();
        await publisher
            .DidNotReceive()
            .PublishAsync(
                Arg.Any<CacheInvalidationMessage>(),
                Arg.Any<PublishOptions?>(),
                Arg.Any<CancellationToken>()
            );
    }

    #endregion

    #region U7-8: null-timestamp L2 entry promoted into L1 bounded by DefaultLocalExpiration

    [Fact]
    public async Task should_promote_null_timestamp_l2_entry_into_l1_bounded_by_default_local_expiration_when_configured()
    {
        // given — L2 returns an entry with null LogicalExpiresAt and PhysicalExpiresAt (legacy/unframed).
        //         DefaultLocalExpiration is configured so the promotion path applies.
        var localExp = TimeSpan.FromMinutes(3);
        var value = Faker.Random.Int(1, 1000);
        var l1Options = new InMemoryCacheOptions { CloneValues = true };
        using var l1Cache = new InMemoryCache(_timeProvider, l1Options);

        using var l2 = new NullTimestampL2Adapter<int>(value);

        var publisher = Substitute.For<IBus>();
        publisher
            .PublishAsync(Arg.Any<CacheInvalidationMessage>(), Arg.Any<PublishOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var cache = new HybridCache(
            l1Cache,
            l2,
            publisher,
            new HybridCacheOptions { DefaultLocalExpiration = localExp },
            timeProvider: _timeProvider
        );
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // when — composite read (TryGetEntryAsync) triggers promotion of the null-timestamp L2 entry
        var entry = await ((IFactoryCacheStore)cache).TryGetEntryAsync<int>(key, AbortToken);

        // then — entry found with the value from L2
        entry.Found.Should().BeTrue("the null-timestamp L2 entry must be surfaced");
        entry.Value.Should().Be(value);

        // and — L1 must have been populated with the entry bounded by now + DefaultLocalExpiration
        var l1Entry = await ((IFactoryCacheStore)l1Cache).TryGetEntryAsync<int>(key, AbortToken);
        l1Entry
            .Found.Should()
            .BeTrue("null-timestamp L2 entry must be promoted into L1 when DefaultLocalExpiration is configured");

        var ceiling = now.Add(localExp);
        l1Entry
            .LogicalExpiresAt.Should()
            .NotBeNull("promoted entry must have a logical expiry set to the local ceiling");
        l1Entry
            .LogicalExpiresAt!.Value.Should()
            .BeCloseTo(ceiling, TimeSpan.FromSeconds(1), "logical expiry must be ≈ now + DefaultLocalExpiration");
        l1Entry
            .PhysicalExpiresAt.Should()
            .NotBeNull("promoted entry must have a physical expiry set to the local ceiling");
        l1Entry
            .PhysicalExpiresAt!.Value.Should()
            .BeCloseTo(ceiling, TimeSpan.FromSeconds(1), "physical expiry must be ≈ now + DefaultLocalExpiration");
    }

    [Fact]
    public async Task should_not_promote_null_timestamp_l2_entry_into_l1_when_no_default_local_expiration()
    {
        // given — L2 returns a null-timestamp entry but DefaultLocalExpiration is not configured.
        //         Without a ceiling there is no finite bound to apply, so the entry must NOT be
        //         cached into L1 (it would live forever in process memory).
        var value = Faker.Random.Int(1, 1000);
        var l1Options = new InMemoryCacheOptions { CloneValues = true };
        using var l1Cache = new InMemoryCache(_timeProvider, l1Options);

        using var l2 = new NullTimestampL2Adapter<int>(value);

        var publisher = Substitute.For<IBus>();
        publisher
            .PublishAsync(Arg.Any<CacheInvalidationMessage>(), Arg.Any<PublishOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var cache = new HybridCache(
            l1Cache,
            l2,
            publisher,
            new HybridCacheOptions { DefaultLocalExpiration = null },
            timeProvider: _timeProvider
        );
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);

        // when — composite read triggers the null-timestamp branch
        var entry = await ((IFactoryCacheStore)cache).TryGetEntryAsync<int>(key, AbortToken);

        // then — entry still surfaced to caller (coordinator needs it)
        entry.Found.Should().BeTrue();
        entry.Value.Should().Be(value);

        // but — L1 must NOT have been populated (no finite bound exists)
        var l1Entry = await ((IFactoryCacheStore)l1Cache).TryGetEntryAsync<int>(key, AbortToken);
        l1Entry
            .Found.Should()
            .BeFalse(
                "without DefaultLocalExpiration there is no finite bound, "
                    + "so null-timestamp L2 entries must not be promoted into L1"
            );
    }

    #endregion
}
