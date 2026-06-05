// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

// ReSharper disable AccessToDisposedClosure
public sealed class HybridCacheFailSafeTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();

    private (HybridCache cache, IInMemoryCache l1, IRemoteCache l2, IBus publisher) _CreateCache(
        HybridCacheOptions? options = null
    )
    {
        options ??= new HybridCacheOptions();
        var l1Options = new InMemoryCacheOptions { CloneValues = true };
        var l1 = new InMemoryCache(_timeProvider, l1Options);

        var l2Options = new InMemoryCacheOptions { CloneValues = true };
        var l2 = new InMemoryRemoteCacheAdapter(new InMemoryCache(_timeProvider, l2Options));

        var publisher = Substitute.For<IBus>();
        publisher
            .PublishAsync(Arg.Any<CacheInvalidationMessage>(), Arg.Any<PublishOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var cache = new HybridCache(l1, l2, publisher, options, timeProvider: _timeProvider);

        return (cache, l1, l2, publisher);
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
        var logicallyExpiredAt = now.AddMinutes(-1);   // already stale
        var physicallyExpiredAt = now.AddHours(1);     // still physically held
        await ((IFactoryCacheStore)l1).SetEntryAsync(
            key,
            staleValue,
            isNull: false,
            logicallyExpiredAt,
            physicallyExpiredAt,
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
            staleValue,
            isNull: false,
            logicallyExpiredAt,
            physicallyExpiredAt,
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
            staleValue,
            isNull: false,
            logicallyExpiredAt,
            physicallyExpiredAt,
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
        var (cache, l1, l2, publisher) = _CreateCache(
            new HybridCacheOptions { DefaultLocalExpiration = localCap }
        );
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        var value = Faker.Random.Int(1, 1000);

        var opts = _FailSafeOptions(
            duration: duration,
            failSafeMaxDuration: TimeSpan.FromHours(2)
        );

        // when — factory succeeds
        var result = await cache.GetOrAddAsync(
            key,
            _ => new ValueTask<int?>(value),
            opts,
            AbortToken
        );

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
        (l2Entry.PhysicalExpiresAt!.Value - now).Should()
            .BeGreaterThan(duration, "L2 must hold the fail-safe physical reserve beyond logical TTL");

        // L1 expiration must be bounded by DefaultLocalExpiration
        var l1Exp = await l1.GetExpirationAsync(key, AbortToken);
        l1Exp.Should().HaveValue();
        l1Exp!.Value.Should()
            .BeLessThanOrEqualTo(localCap.Add(TimeSpan.FromSeconds(1)),
                "L1 expiration must be capped by DefaultLocalExpiration");

        // No backplane publish on factory-success path (GetOrAddAsync does not publish)
        await publisher
            .DidNotReceive()
            .PublishAsync(
                Arg.Any<CacheInvalidationMessage>(),
                Arg.Any<PublishOptions?>(),
                Arg.Any<CancellationToken>()
            );
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
            staleValue,
            isNull: false,
            logicallyExpiredAt,
            physicallyExpiredAt,
            AbortToken
        );

        // when — drive the composite read primitive directly (no factory / activation / restamp involved)
        var entry = await ((IFactoryCacheStore)cache).TryGetEntryAsync<int>(key, AbortToken);

        // then — the entry is surfaced so the coordinator can use it as a stale candidate
        entry.Found.Should().BeTrue("the composite read must surface the L2 reserve as a stale candidate");
        entry.Value.Should().Be(staleValue);

        // and — the read path must NOT have promoted the logically-expired reserve into L1
        var l1Entry = await ((IFactoryCacheStore)l1).TryGetEntryAsync<int>(key, AbortToken);
        l1Entry.Found.Should()
            .BeFalse(
                "the read-path guard must not promote a logically-expired L2 reserve into L1 (#9): "
                    + "promoting on every fail-safe read amplifies L1 writes and can overwrite a newer L1 reserve"
            );
    }

    #endregion

    #region U7-7: fail-safe activation refreshes L1 with a throttled, logically-fresh entry (FusionCache parity)

    [Fact]
    public async Task should_refresh_l1_with_throttled_entry_when_failsafe_activates()
    {
        // given — only L2 holds the reserve; L1 is empty
        var throttle = TimeSpan.FromSeconds(30);
        var (cache, _, l2, _) = _CreateCache(new HybridCacheOptions { DefaultLocalExpiration = null });
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        var staleValue = Faker.Random.Int(1, 100);
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var logicallyExpiredAt = now.AddMinutes(-1);
        var physicallyExpiredAt = now.AddHours(1);
        await ((IFactoryCacheStore)l2).SetEntryAsync(
            key,
            staleValue,
            isNull: false,
            logicallyExpiredAt,
            physicallyExpiredAt,
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

        // and — fail-safe refreshed L1 with a logically-fresh throttle entry (FusionCache parity, KTD-4):
        // logical ≈ now + FailSafeThrottleDuration (in the future, within the physical reserve)
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

        // a subsequent read within the throttle window is a normal L1 hit — fresh, factory not invoked
        second.HasValue.Should().BeTrue();
        second.Value.Should().Be(staleValue);
        second.IsStale.Should().BeFalse("the throttle entry is logically fresh, so the read is a normal L1 hit");
        factoryCallCount.Should()
            .Be(1, "the throttle window must absorb the read without re-invoking the factory");
    }

    #endregion

    #region Test doubles

    /// <summary>Simple adapter to use InMemoryCache as IRemoteCache for testing.</summary>
    private sealed class InMemoryRemoteCacheAdapter(InMemoryCache cache) : IRemoteCache, IFactoryCacheStore
    {
        public ValueTask<CacheValue<T>> GetOrAddAsync<T>(
            string key,
            Func<CancellationToken, ValueTask<T?>> factory,
            CacheEntryOptions options,
            CancellationToken cancellationToken = default
        ) => cache.GetOrAddAsync(key, factory, options, cancellationToken);

        public ValueTask<CacheStoreEntry<T>> TryGetEntryAsync<T>(string key, CancellationToken cancellationToken) =>
            ((IFactoryCacheStore)cache).TryGetEntryAsync<T>(key, cancellationToken);

        public ValueTask SetEntryAsync<T>(
            string key,
            T? value,
            bool isNull,
            DateTime logicalExpiresAt,
            DateTime physicalExpiresAt,
            CancellationToken cancellationToken
        ) =>
            ((IFactoryCacheStore)cache)
                .SetEntryAsync(key, value, isNull, logicalExpiresAt, physicalExpiresAt, cancellationToken);

        public ValueTask<bool> UpsertAsync<T>(string key, T? value, TimeSpan? expiration, CancellationToken ct = default) =>
            cache.UpsertAsync(key, value, expiration, ct);

        public ValueTask<int> UpsertAllAsync<T>(IDictionary<string, T> value, TimeSpan? expiration, CancellationToken ct = default) =>
            cache.UpsertAllAsync(value, expiration, ct);

        public ValueTask<bool> TryInsertAsync<T>(string key, T? value, TimeSpan? expiration, CancellationToken ct = default) =>
            cache.TryInsertAsync(key, value, expiration, ct);

        public ValueTask<bool> TryReplaceAsync<T>(string key, T? value, TimeSpan? expiration, CancellationToken ct = default) =>
            cache.TryReplaceAsync(key, value, expiration, ct);

        public ValueTask<bool> TryReplaceIfEqualAsync<T>(string key, T? expected, T? value, TimeSpan? expiration, CancellationToken ct = default) =>
            cache.TryReplaceIfEqualAsync(key, expected, value, expiration, ct);

        public ValueTask<double> IncrementAsync(string key, double amount, TimeSpan? expiration, CancellationToken ct = default) =>
            cache.IncrementAsync(key, amount, expiration, ct);

        public ValueTask<long> IncrementAsync(string key, long amount, TimeSpan? expiration, CancellationToken ct = default) =>
            cache.IncrementAsync(key, amount, expiration, ct);

        public ValueTask<double> SetIfHigherAsync(string key, double value, TimeSpan? expiration, CancellationToken ct = default) =>
            cache.SetIfHigherAsync(key, value, expiration, ct);

        public ValueTask<long> SetIfHigherAsync(string key, long value, TimeSpan? expiration, CancellationToken ct = default) =>
            cache.SetIfHigherAsync(key, value, expiration, ct);

        public ValueTask<double> SetIfLowerAsync(string key, double value, TimeSpan? expiration, CancellationToken ct = default) =>
            cache.SetIfLowerAsync(key, value, expiration, ct);

        public ValueTask<long> SetIfLowerAsync(string key, long value, TimeSpan? expiration, CancellationToken ct = default) =>
            cache.SetIfLowerAsync(key, value, expiration, ct);

        public ValueTask<long> SetAddAsync<T>(string key, IEnumerable<T> value, TimeSpan? expiration, CancellationToken ct = default) =>
            cache.SetAddAsync(key, value, expiration, ct);

        public ValueTask<IDictionary<string, CacheValue<T>>> GetAllAsync<T>(IEnumerable<string> keys, CancellationToken ct = default) =>
            cache.GetAllAsync<T>(keys, ct);

        public ValueTask<IDictionary<string, CacheValue<T>>> GetByPrefixAsync<T>(string prefix, CancellationToken ct = default) =>
            cache.GetByPrefixAsync<T>(prefix, ct);

        public ValueTask<IReadOnlyList<string>> GetAllKeysByPrefixAsync(string prefix, CancellationToken ct = default) =>
            cache.GetAllKeysByPrefixAsync(prefix, ct);

        public ValueTask<CacheValue<T>> GetAsync<T>(string key, CancellationToken ct = default) =>
            cache.GetAsync<T>(key, ct);

        public ValueTask<long> GetCountAsync(string prefix = "", CancellationToken ct = default) =>
            cache.GetCountAsync(prefix, ct);

        public ValueTask<bool> ExistsAsync(string key, CancellationToken ct = default) =>
            cache.ExistsAsync(key, ct);

        public ValueTask<TimeSpan?> GetExpirationAsync(string key, CancellationToken ct = default) =>
            cache.GetExpirationAsync(key, ct);

        public ValueTask<CacheValue<ICollection<T>>> GetSetAsync<T>(string key, int? pageIndex = null, int pageSize = 100, CancellationToken ct = default) =>
            cache.GetSetAsync<T>(key, pageIndex, pageSize, ct);

        public ValueTask<bool> RemoveAsync(string key, CancellationToken ct = default) =>
            cache.RemoveAsync(key, ct);

        public ValueTask<bool> RemoveIfEqualAsync<T>(string key, T? expected, CancellationToken ct = default) =>
            cache.RemoveIfEqualAsync(key, expected, ct);

        public ValueTask<int> RemoveAllAsync(IEnumerable<string> keys, CancellationToken ct = default) =>
            cache.RemoveAllAsync(keys, ct);

        public ValueTask<int> RemoveByPrefixAsync(string prefix, CancellationToken ct = default) =>
            cache.RemoveByPrefixAsync(prefix, ct);

        public ValueTask<long> SetRemoveAsync<T>(string key, IEnumerable<T> value, TimeSpan? expiration, CancellationToken ct = default) =>
            cache.SetRemoveAsync(key, value, expiration, ct);

        public ValueTask FlushAsync(CancellationToken ct = default) =>
            cache.FlushAsync(ct);
    }

    /// <summary>
    /// An L2 remote cache whose read (TryGetEntryAsync) always throws to simulate a down store.
    /// Write operations are no-ops so the factory-success path still works if needed.
    /// </summary>
    private sealed class ThrowingReadRemoteCache(TimeProvider timeProvider) : IRemoteCache, IFactoryCacheStore, IDisposable
    {
        private readonly InMemoryCache _inner = new(timeProvider, new InMemoryCacheOptions());

        public ValueTask<CacheStoreEntry<T>> TryGetEntryAsync<T>(string key, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("L2 store is unavailable");

        public ValueTask SetEntryAsync<T>(
            string key,
            T? value,
            bool isNull,
            DateTime logicalExpiresAt,
            DateTime physicalExpiresAt,
            CancellationToken cancellationToken
        ) =>
            // No-op: writes are silently dropped (non-fatal in HybridCache.SetEntryAsync)
            ValueTask.CompletedTask;

        public ValueTask<CacheValue<T>> GetOrAddAsync<T>(
            string key,
            Func<CancellationToken, ValueTask<T?>> factory,
            CacheEntryOptions options,
            CancellationToken ct = default
        ) => throw new InvalidOperationException("L2 store is unavailable");

        public ValueTask<bool> UpsertAsync<T>(string key, T? value, TimeSpan? expiration, CancellationToken ct = default) =>
            new(false);

        public ValueTask<int> UpsertAllAsync<T>(IDictionary<string, T> value, TimeSpan? expiration, CancellationToken ct = default) =>
            new(0);

        public ValueTask<bool> TryInsertAsync<T>(string key, T? value, TimeSpan? expiration, CancellationToken ct = default) =>
            new(false);

        public ValueTask<bool> TryReplaceAsync<T>(string key, T? value, TimeSpan? expiration, CancellationToken ct = default) =>
            new(false);

        public ValueTask<bool> TryReplaceIfEqualAsync<T>(string key, T? expected, T? value, TimeSpan? expiration, CancellationToken ct = default) =>
            new(false);

        public ValueTask<double> IncrementAsync(string key, double amount, TimeSpan? expiration, CancellationToken ct = default) =>
            new(0d);

        public ValueTask<long> IncrementAsync(string key, long amount, TimeSpan? expiration, CancellationToken ct = default) =>
            new(0L);

        public ValueTask<double> SetIfHigherAsync(string key, double value, TimeSpan? expiration, CancellationToken ct = default) =>
            new(0d);

        public ValueTask<long> SetIfHigherAsync(string key, long value, TimeSpan? expiration, CancellationToken ct = default) =>
            new(0L);

        public ValueTask<double> SetIfLowerAsync(string key, double value, TimeSpan? expiration, CancellationToken ct = default) =>
            new(0d);

        public ValueTask<long> SetIfLowerAsync(string key, long value, TimeSpan? expiration, CancellationToken ct = default) =>
            new(0L);

        public ValueTask<long> SetAddAsync<T>(string key, IEnumerable<T> value, TimeSpan? expiration, CancellationToken ct = default) =>
            new(0L);

        public ValueTask<CacheValue<T>> GetAsync<T>(string key, CancellationToken ct = default) =>
            new(CacheValue<T>.NoValue);

        public ValueTask<IDictionary<string, CacheValue<T>>> GetAllAsync<T>(IEnumerable<string> keys, CancellationToken ct = default) =>
            new((IDictionary<string, CacheValue<T>>)new Dictionary<string, CacheValue<T>>(StringComparer.Ordinal));

        public ValueTask<IDictionary<string, CacheValue<T>>> GetByPrefixAsync<T>(string prefix, CancellationToken ct = default) =>
            new((IDictionary<string, CacheValue<T>>)new Dictionary<string, CacheValue<T>>(StringComparer.Ordinal));

        public ValueTask<IReadOnlyList<string>> GetAllKeysByPrefixAsync(string prefix, CancellationToken ct = default) =>
            new((IReadOnlyList<string>)Array.Empty<string>());

        public ValueTask<long> GetCountAsync(string prefix = "", CancellationToken ct = default) =>
            new(0L);

        public ValueTask<bool> ExistsAsync(string key, CancellationToken ct = default) =>
            new(false);

        public ValueTask<TimeSpan?> GetExpirationAsync(string key, CancellationToken ct = default) =>
            new((TimeSpan?)null);

        public ValueTask<CacheValue<ICollection<T>>> GetSetAsync<T>(string key, int? pageIndex = null, int pageSize = 100, CancellationToken ct = default) =>
            new(CacheValue<ICollection<T>>.NoValue);

        public ValueTask<bool> RemoveAsync(string key, CancellationToken ct = default) =>
            new(false);

        public ValueTask<bool> RemoveIfEqualAsync<T>(string key, T? expected, CancellationToken ct = default) =>
            new(false);

        public ValueTask<int> RemoveAllAsync(IEnumerable<string> keys, CancellationToken ct = default) =>
            new(0);

        public ValueTask<int> RemoveByPrefixAsync(string prefix, CancellationToken ct = default) =>
            new(0);

        public ValueTask<long> SetRemoveAsync<T>(string key, IEnumerable<T> value, TimeSpan? expiration, CancellationToken ct = default) =>
            new(0L);

        public ValueTask FlushAsync(CancellationToken ct = default) => ValueTask.CompletedTask;

        public void Dispose() => _inner.Dispose();
    }

    #endregion
}
