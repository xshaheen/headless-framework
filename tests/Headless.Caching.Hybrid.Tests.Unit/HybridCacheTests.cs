// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class HybridCacheTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();

    // The HybridCache returned here is disposed per test (via `await using` or an explicit DisposeAsync), but it
    // does not own the injected L1/L2 stores. This fixture collects those raw InMemoryCache instances and disposes
    // them at teardown.
    private readonly List<object> _disposables = [];

    private (HybridCache cache, IInMemoryCache l1, IRemoteCache l2, IBus publisher) _CreateCache(
        HybridCacheOptions? options = null
    )
    {
        options ??= new HybridCacheOptions();
        var l1Options = new InMemoryCacheOptions { CloneValues = true };
        var l1 = new InMemoryCache(_timeProvider, l1Options);

        // Create a separate in-memory cache as the "distributed" cache for testing
        var l2Options = new InMemoryCacheOptions { CloneValues = true };
        var inMemoryCache = new InMemoryCache(_timeProvider, l2Options);
        var l2 = new InMemoryRemoteCacheAdapter(inMemoryCache);

        var publisher = Substitute.For<IBus>();
        publisher
            .PublishAsync(Arg.Any<CacheInvalidationMessage>(), Arg.Any<PublishOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var cache = new HybridCache(l1, l2, publisher, options, timeProvider: _timeProvider);

        _disposables.Add(l1);
        _disposables.Add(inMemoryCache);

        return (cache, l1, l2, publisher);
    }

    #region GetOrAddAsync - Basic

    [Fact]
    public async Task should_return_value_from_l1_when_exists_in_l1()
    {
        // given
        var (cache, l1, _, _) = _CreateCache();
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        var value = Faker.Random.Int();
        await l1.UpsertAsync(key, value, TimeSpan.FromMinutes(5), AbortToken);
        var factoryCalled = false;

        // when
        var result = await cache.GetOrAddAsync(
            key,
            _ =>
            {
                factoryCalled = true;
                return new ValueTask<int?>(999);
            },
            TimeSpan.FromMinutes(5),
            AbortToken
        );

        // then
        result.HasValue.Should().BeTrue();
        result.Value.Should().Be(value);
        factoryCalled.Should().BeFalse("factory should not be called on L1 hit");
        cache.LocalCacheHits.Should().Be(1);
    }

    [Fact]
    public async Task should_return_value_from_l2_and_populate_l1_when_l1_miss_but_l2_hit()
    {
        // given
        var (cache, l1, l2, _) = _CreateCache();
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        var value = Faker.Random.Int();
        await l2.UpsertAsync(key, value, TimeSpan.FromMinutes(5), AbortToken);
        var factoryCalled = false;

        // when
        var result = await cache.GetOrAddAsync(
            key,
            _ =>
            {
                factoryCalled = true;
                return new ValueTask<int?>(999);
            },
            TimeSpan.FromMinutes(5),
            AbortToken
        );

        // then
        result.HasValue.Should().BeTrue();
        result.Value.Should().Be(value);
        factoryCalled.Should().BeFalse("factory should not be called on L2 hit");

        // Verify L1 was populated
        var l1Result = await l1.GetAsync<int>(key, AbortToken);
        l1Result.HasValue.Should().BeTrue();
        l1Result.Value.Should().Be(value);
    }

    [Fact]
    public async Task should_round_trip_entry_metadata_through_composite_store()
    {
        // given
        var (cache, l1, l2, _) = _CreateCache();
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var entry = new CacheStoreEntryWrite<int>
        {
            Value = Faker.Random.Int(1, 100),
            IsNull = false,
            LogicalExpiresAt = now.AddMinutes(5),
            PhysicalExpiresAt = now.AddMinutes(10),
            EagerRefreshAt = now.AddMinutes(4),
            ETag = "W/\"v42\"",
            LastModifiedAt = now.AddMinutes(-30),
            Tags = ["tenant:1", "products"],
        };

        // when — write through the composite store (both tiers), then read it back
        await ((IFactoryCacheStore)cache).SetEntryAsync(key, in entry, AbortToken);
        var roundTripped = await ((IFactoryCacheStore)cache).TryGetEntryAsync<int>(key, AbortToken);

        // then — the composite read surfaces the metadata unchanged
        roundTripped.Found.Should().BeTrue();
        roundTripped.Value.Should().Be(entry.Value);
        roundTripped.EagerRefreshAt.Should().Be(entry.EagerRefreshAt);
        roundTripped.ETag.Should().Be(entry.ETag);
        roundTripped.LastModifiedAt.Should().Be(entry.LastModifiedAt);
        roundTripped.Tags.Should().BeEquivalentTo("tenant:1", "products");

        // and — both tiers persisted the metadata, not just the one that served the read
        var l1Entry = await ((IFactoryCacheStore)l1).TryGetEntryAsync<int>(key, AbortToken);
        l1Entry.ETag.Should().Be(entry.ETag);
        l1Entry.EagerRefreshAt.Should().Be(entry.EagerRefreshAt);
        l1Entry.LastModifiedAt.Should().Be(entry.LastModifiedAt);
        l1Entry.Tags.Should().BeEquivalentTo("tenant:1", "products");

        var l2Entry = await ((IFactoryCacheStore)l2).TryGetEntryAsync<int>(key, AbortToken);
        l2Entry.ETag.Should().Be(entry.ETag);
        l2Entry.EagerRefreshAt.Should().Be(entry.EagerRefreshAt);
        l2Entry.LastModifiedAt.Should().Be(entry.LastModifiedAt);
        l2Entry.Tags.Should().BeEquivalentTo("tenant:1", "products");
    }

    [Fact]
    public async Task should_return_fresh_l2_value_when_l1_has_stale_reserve()
    {
        // given
        var (cache, l1, l2, _) = _CreateCache();
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        var staleValue = Faker.Random.Int(1, 100);
        var freshValue = Faker.Random.Int(101, 200);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        await ((IFactoryCacheStore)l1).SetEntryAsync(
            key,
            new CacheStoreEntryWrite<int>
            {
                Value = staleValue,
                IsNull = false,
                LogicalExpiresAt = now.AddMinutes(-1),
                PhysicalExpiresAt = now.AddMinutes(5),
            },
            AbortToken
        );
        await l2.UpsertAsync(key, freshValue, TimeSpan.FromMinutes(5), AbortToken);
        var factoryCalled = false;

        // when
        var result = await cache.GetOrAddAsync(
            key,
            _ =>
            {
                factoryCalled = true;
                return new ValueTask<int?>(999);
            },
            TimeSpan.FromMinutes(5),
            AbortToken
        );

        // then
        result.HasValue.Should().BeTrue();
        result.Value.Should().Be(freshValue);
        factoryCalled.Should().BeFalse("a fresh L2 value should win over a stale L1 reserve");
    }

    [Fact]
    public async Task should_call_factory_and_populate_both_caches_when_complete_miss()
    {
        // given
        var (cache, l1, l2, _) = _CreateCache();
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        var value = Faker.Random.Int();
        var factoryCallCount = 0;

        // when
        var result = await cache.GetOrAddAsync(
            key,
            _ =>
            {
                factoryCallCount++;
                return new ValueTask<int?>(value);
            },
            TimeSpan.FromMinutes(5),
            AbortToken
        );

        // then
        result.HasValue.Should().BeTrue();
        result.Value.Should().Be(value);
        factoryCallCount.Should().Be(1);

        // Verify both caches were populated
        var l1Result = await l1.GetAsync<int>(key, AbortToken);
        l1Result.HasValue.Should().BeTrue();
        l1Result.Value.Should().Be(value);

        var l2Result = await l2.GetAsync<int>(key, AbortToken);
        l2Result.HasValue.Should().BeTrue();
        l2Result.Value.Should().Be(value);
    }

    [Fact]
    public async Task should_use_local_expiration_for_l1_when_configured()
    {
        // given
        var localExpiration = TimeSpan.FromMinutes(1);
        var l2Expiration = TimeSpan.FromMinutes(10);
        var (cache, l1, l2, _) = _CreateCache(new HybridCacheOptions { DefaultLocalExpiration = localExpiration });
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        var value = Faker.Random.Int();

        // when
        await cache.GetOrAddAsync(key, _ => new ValueTask<int?>(value), l2Expiration, AbortToken);

        // then
        var l1Exp = await l1.GetExpirationAsync(key, AbortToken);
        var l2Exp = await l2.GetExpirationAsync(key, AbortToken);

        // L1 should have shorter expiration
        l1Exp.Should().BeLessThanOrEqualTo(localExpiration);
        // L2 should have the requested expiration
        l2Exp.Should().BeLessThanOrEqualTo(l2Expiration);
    }

    #endregion

    #region GetOrAddAsync - Stampede Protection

    [Fact]
    public async Task should_call_factory_only_once_when_concurrent_requests_for_same_key()
    {
        // given
        var (cache, _, _, _) = _CreateCache();
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        var value = Faker.Random.Int();
        var factoryCallCount = 0;
        var factoryDelay = TimeSpan.FromMilliseconds(100);

        // when - launch multiple concurrent requests
        var tasks = Enumerable
            .Range(0, 10)
            .Select(_ =>
                cache
                    .GetOrAddAsync(
                        key,
                        async ct =>
                        {
                            Interlocked.Increment(ref factoryCallCount);
                            await Task.Delay(factoryDelay, ct);
                            return value;
                        },
                        TimeSpan.FromMinutes(5),
                        AbortToken
                    )
                    .AsTask()
            )
            .ToArray();

        var results = await Task.WhenAll(tasks);

        // then
        factoryCallCount.Should().Be(1, "factory should only be called once due to stampede protection");
        results
            .Should()
            .AllSatisfy(r =>
            {
                r.HasValue.Should().BeTrue();
                r.Value.Should().Be(value);
            });
    }

    [Fact]
    public async Task should_double_check_l1_and_l2_after_acquiring_lock()
    {
        // given
        var (cache, l1, l2, _) = _CreateCache();
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        var l2Value = Faker.Random.Int();
        var factoryCalled = false;

        // Simulate another instance populating L2 while we wait for lock
        // We do this by having the factory check if L2 was populated and only then set the flag
        await l2.UpsertAsync(key, l2Value, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.GetOrAddAsync(
            key,
            _ =>
            {
                factoryCalled = true;
                return new ValueTask<int?>(999);
            },
            TimeSpan.FromMinutes(5),
            AbortToken
        );

        // then
        result.HasValue.Should().BeTrue();
        result.Value.Should().Be(l2Value, "should return L2 value, not factory value");
        factoryCalled.Should().BeFalse("factory should not be called because L2 had the value");
    }

    #endregion

    #region RemoveAsync

    [Fact]
    public async Task should_remove_from_both_caches_and_publish_invalidation()
    {
        // given
        var (cache, l1, l2, publisher) = _CreateCache();
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        var value = Faker.Random.Int();
        await l1.UpsertAsync(key, value, TimeSpan.FromMinutes(5), AbortToken);
        await l2.UpsertAsync(key, value, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var removed = await cache.RemoveAsync(key, AbortToken);

        // then
        removed.Should().BeTrue();

        var l1Result = await l1.GetAsync<int>(key, AbortToken);
        l1Result.HasValue.Should().BeFalse();

        var l2Result = await l2.GetAsync<int>(key, AbortToken);
        l2Result.HasValue.Should().BeFalse();

        await publisher
            .Received(1)
            .PublishAsync(
                Arg.Is<CacheInvalidationMessage>(m => m.Key == key),
                Arg.Is<PublishOptions?>(options => options == null),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_not_publish_invalidation_when_key_does_not_exist()
    {
        // given
        var (cache, _, _, publisher) = _CreateCache();
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);

        // when
        var removed = await cache.RemoveAsync(key, AbortToken);

        // then
        removed.Should().BeFalse();
        await publisher
            .DidNotReceive()
            .PublishAsync(
                Arg.Any<CacheInvalidationMessage>(),
                Arg.Any<PublishOptions?>(),
                Arg.Any<CancellationToken>()
            );
    }

    #endregion

    #region RemoveByPrefixAsync

    [Fact]
    public async Task should_remove_by_prefix_from_both_caches_and_publish()
    {
        // given
        var (cache, l1, l2, publisher) = _CreateCache();
        await using var _ = cache;

        const string prefix = "test:";
        await l1.UpsertAsync($"{prefix}key1", 1, TimeSpan.FromMinutes(5), AbortToken);
        await l1.UpsertAsync($"{prefix}key2", 2, TimeSpan.FromMinutes(5), AbortToken);
        await l2.UpsertAsync($"{prefix}key1", 1, TimeSpan.FromMinutes(5), AbortToken);
        await l2.UpsertAsync($"{prefix}key2", 2, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var removed = await cache.RemoveByPrefixAsync(prefix, AbortToken);

        // then
        removed.Should().Be(2);

        await publisher
            .Received(1)
            .PublishAsync(
                Arg.Is<CacheInvalidationMessage>(m => m.Prefix == prefix),
                Arg.Is<PublishOptions?>(options => options == null),
                Arg.Any<CancellationToken>()
            );
    }

    #endregion

    #region FlushAsync

    [Fact]
    public async Task should_flush_both_caches_and_publish_flush_all()
    {
        // given
        var (cache, l1, l2, publisher) = _CreateCache();
        await using var _ = cache;

        await l1.UpsertAsync("key1", 1, TimeSpan.FromMinutes(5), AbortToken);
        await l2.UpsertAsync("key2", 2, TimeSpan.FromMinutes(5), AbortToken);

        // when
        await cache.FlushAsync(AbortToken);

        // then
        var l1Count = await l1.GetCountAsync(cancellationToken: AbortToken);
        var l2Count = await l2.GetCountAsync(cancellationToken: AbortToken);
        l1Count.Should().Be(0);
        l2Count.Should().Be(0);

        await publisher
            .Received(1)
            .PublishAsync(
                Arg.Is<CacheInvalidationMessage>(m => m.FlushAll),
                Arg.Is<PublishOptions?>(options => options == null),
                Arg.Any<CancellationToken>()
            );
    }

    #endregion

    #region Invalidation Handler

    [Fact]
    public async Task should_remove_from_l1_when_receiving_invalidation_from_other_instance()
    {
        // given
        var options = new HybridCacheOptions { InstanceId = "instance-1" };
        var (cache, l1, _, _) = _CreateCache(options);
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        await l1.UpsertAsync(key, 42, TimeSpan.FromMinutes(5), AbortToken);

        var message = new CacheInvalidationMessage
        {
            InstanceId = "instance-2", // Different instance
            Key = key,
        };

        // when
        await cache.HandleInvalidationAsync(message, AbortToken);

        // then
        var result = await l1.GetAsync<int>(key, AbortToken);
        result.HasValue.Should().BeFalse();
        cache.InvalidateCacheCalls.Should().Be(1);
    }

    [Fact]
    public async Task should_ignore_self_originated_invalidation_messages()
    {
        // given
        const string instanceId = "instance-1";
        var options = new HybridCacheOptions { InstanceId = instanceId };
        var (cache, l1, _, _) = _CreateCache(options);
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        await l1.UpsertAsync(key, 42, TimeSpan.FromMinutes(5), AbortToken);

        var message = new CacheInvalidationMessage
        {
            InstanceId = instanceId, // Same instance
            Key = key,
        };

        // when
        await cache.HandleInvalidationAsync(message, AbortToken);

        // then
        var result = await l1.GetAsync<int>(key, AbortToken);
        result.HasValue.Should().BeTrue("self-originated message should be ignored");
        cache.InvalidateCacheCalls.Should().Be(0);
    }

    [Fact]
    public async Task should_flush_l1_when_receiving_flush_all_invalidation()
    {
        // given
        var options = new HybridCacheOptions { InstanceId = "instance-1" };
        var (cache, l1, _, _) = _CreateCache(options);
        await using var _ = cache;

        await l1.UpsertAsync("key1", 1, TimeSpan.FromMinutes(5), AbortToken);
        await l1.UpsertAsync("key2", 2, TimeSpan.FromMinutes(5), AbortToken);

        var message = new CacheInvalidationMessage { InstanceId = "instance-2", FlushAll = true };

        // when
        await cache.HandleInvalidationAsync(message, AbortToken);

        // then
        var count = await l1.GetCountAsync(cancellationToken: AbortToken);
        count.Should().Be(0);
    }

    [Fact]
    public async Task should_remove_by_prefix_when_receiving_prefix_invalidation()
    {
        // given
        var options = new HybridCacheOptions { InstanceId = "instance-1" };
        var (cache, l1, _, _) = _CreateCache(options);
        await using var _ = cache;

        const string prefix = "user:";
        await l1.UpsertAsync($"{prefix}1", 1, TimeSpan.FromMinutes(5), AbortToken);
        await l1.UpsertAsync($"{prefix}2", 2, TimeSpan.FromMinutes(5), AbortToken);
        await l1.UpsertAsync("other:1", 3, TimeSpan.FromMinutes(5), AbortToken);

        var message = new CacheInvalidationMessage { InstanceId = "instance-2", Prefix = prefix };

        // when
        await cache.HandleInvalidationAsync(message, AbortToken);

        // then
        var result1 = await l1.GetAsync<int>($"{prefix}1", AbortToken);
        var result2 = await l1.GetAsync<int>($"{prefix}2", AbortToken);
        var resultOther = await l1.GetAsync<int>("other:1", AbortToken);

        result1.HasValue.Should().BeFalse();
        result2.HasValue.Should().BeFalse();
        resultOther.HasValue.Should().BeTrue("keys without prefix should not be removed");
    }

    [Fact]
    public async Task should_remove_multiple_keys_when_receiving_keys_invalidation()
    {
        // given
        var options = new HybridCacheOptions { InstanceId = "instance-1" };
        var (cache, l1, _, _) = _CreateCache(options);
        await using var _ = cache;

        await l1.UpsertAsync("key1", 1, TimeSpan.FromMinutes(5), AbortToken);
        await l1.UpsertAsync("key2", 2, TimeSpan.FromMinutes(5), AbortToken);
        await l1.UpsertAsync("key3", 3, TimeSpan.FromMinutes(5), AbortToken);

        var message = new CacheInvalidationMessage { InstanceId = "instance-2", Keys = ["key1", "key2"] };

        // when
        await cache.HandleInvalidationAsync(message, AbortToken);

        // then
        var result1 = await l1.GetAsync<int>("key1", AbortToken);
        var result2 = await l1.GetAsync<int>("key2", AbortToken);
        var result3 = await l1.GetAsync<int>("key3", AbortToken);

        result1.HasValue.Should().BeFalse();
        result2.HasValue.Should().BeFalse();
        result3.HasValue.Should().BeTrue("key3 was not in the invalidation list");
    }

    #endregion

    #region Exception Handling

    [Fact]
    public async Task should_still_populate_l1_when_l2_write_fails()
    {
        // given
        var l1Options = new InMemoryCacheOptions { CloneValues = true };
        using var l1 = new InMemoryCache(_timeProvider, l1Options);

        // Use a test double that throws on write but returns values on read
        using var l2 = new FailingWriteRemoteCache(_timeProvider);

        var publisher = Substitute.For<IBus>();
        publisher
            .PublishAsync(Arg.Any<CacheInvalidationMessage>(), Arg.Any<PublishOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var cache = new HybridCache(l1, l2, publisher, new HybridCacheOptions(), timeProvider: _timeProvider);
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        var value = Faker.Random.Int();

        // when
        var result = await cache.GetOrAddAsync(
            key,
            _ => new ValueTask<int?>(value),
            TimeSpan.FromMinutes(5),
            AbortToken
        );

        // then
        result.HasValue.Should().BeTrue();
        result.Value.Should().Be(value);

        // L1 should still be populated despite L2 failure
        var l1Result = await l1.GetAsync<int>(key, AbortToken);
        l1Result.HasValue.Should().BeTrue();
        l1Result.Value.Should().Be(value);
    }

    /// <summary>A distributed cache that throws on write operations but works normally for reads.</summary>
    private sealed class FailingWriteRemoteCache(TimeProvider timeProvider) : IRemoteCache, IDisposable
    {
        private readonly InMemoryCache _cache = new(timeProvider, new InMemoryCacheOptions());

        public CacheEntryOptions? DefaultEntryOptions => null;

        public ValueTask<CacheValue<T>> GetOrAddAsync<T>(
            string key,
            Func<CancellationToken, ValueTask<T?>> factory,
            CacheEntryOptions options,
            CancellationToken ct = default
        ) => throw new InvalidOperationException("L2 write failed");

        public ValueTask<CacheValue<T>> GetOrAddAsync<T>(
            string key,
            Func<CacheFactoryContext<T>, CancellationToken, ValueTask<CacheFactoryResult<T>>> factory,
            CacheEntryOptions options,
            CancellationToken ct = default
        ) => throw new InvalidOperationException("L2 write failed");

        public ValueTask<bool> UpsertAsync<T>(
            string key,
            T? value,
            TimeSpan? expiration,
            CancellationToken ct = default
        ) => throw new InvalidOperationException("L2 write failed");

        public ValueTask<bool> UpsertEntryAsync<T>(
            string key,
            T? value,
            CacheEntryOptions options,
            CancellationToken ct = default
        ) => throw new InvalidOperationException("L2 write failed");

        public ValueTask<int> UpsertAllAsync<T>(
            IDictionary<string, T> value,
            TimeSpan? expiration,
            CancellationToken ct = default
        ) => throw new InvalidOperationException("L2 write failed");

        public ValueTask<bool> TryInsertAsync<T>(
            string key,
            T? value,
            TimeSpan? expiration,
            CancellationToken ct = default
        ) => throw new InvalidOperationException("L2 write failed");

        public ValueTask<bool> TryReplaceAsync<T>(
            string key,
            T? value,
            TimeSpan? expiration,
            CancellationToken ct = default
        ) => throw new InvalidOperationException("L2 write failed");

        public ValueTask<bool> TryReplaceIfEqualAsync<T>(
            string key,
            T? expected,
            T? value,
            TimeSpan? expiration,
            CancellationToken ct = default
        ) => throw new InvalidOperationException("L2 write failed");

        public ValueTask<double> IncrementAsync(
            string key,
            double amount,
            TimeSpan? expiration,
            CancellationToken ct = default
        ) => throw new InvalidOperationException("L2 write failed");

        public ValueTask<long> IncrementAsync(
            string key,
            long amount,
            TimeSpan? expiration,
            CancellationToken ct = default
        ) => throw new InvalidOperationException("L2 write failed");

        public ValueTask<double> SetIfHigherAsync(
            string key,
            double value,
            TimeSpan? expiration,
            CancellationToken ct = default
        ) => throw new InvalidOperationException("L2 write failed");

        public ValueTask<long> SetIfHigherAsync(
            string key,
            long value,
            TimeSpan? expiration,
            CancellationToken ct = default
        ) => throw new InvalidOperationException("L2 write failed");

        public ValueTask<double> SetIfLowerAsync(
            string key,
            double value,
            TimeSpan? expiration,
            CancellationToken ct = default
        ) => throw new InvalidOperationException("L2 write failed");

        public ValueTask<long> SetIfLowerAsync(
            string key,
            long value,
            TimeSpan? expiration,
            CancellationToken ct = default
        ) => throw new InvalidOperationException("L2 write failed");

        public ValueTask<long> SetAddAsync<T>(
            string key,
            IEnumerable<T> value,
            TimeSpan? expiration,
            CancellationToken ct = default
        ) => throw new InvalidOperationException("L2 write failed");

        // Read operations delegate to real cache (returning empty for this test)
        public ValueTask<CacheValue<T>> GetAsync<T>(string key, CancellationToken ct = default) =>
            _cache.GetAsync<T>(key, ct);

        public ValueTask<IDictionary<string, CacheValue<T>>> GetAllAsync<T>(
            IEnumerable<string> keys,
            CancellationToken ct = default
        ) => _cache.GetAllAsync<T>(keys, ct);

        public async ValueTask<CacheValueWithExpiration<T>> GetWithExpirationAsync<T>(
            string key,
            CancellationToken ct = default
        )
        {
            var value = await _cache.GetAsync<T>(key, ct).ConfigureAwait(false);

            if (!value.HasValue)
            {
                return new CacheValueWithExpiration<T>(CacheValue<T>.NoValue, null);
            }

            var expiration = await _cache.GetExpirationAsync(key, ct).ConfigureAwait(false);
            return new CacheValueWithExpiration<T>(value, expiration);
        }

        public async ValueTask<IDictionary<string, CacheValueWithExpiration<T>>> GetAllWithExpirationAsync<T>(
            IEnumerable<string> cacheKeys,
            CancellationToken ct = default
        )
        {
            var values = await _cache.GetAllAsync<T>(cacheKeys, ct).ConfigureAwait(false);
            var result = new Dictionary<string, CacheValueWithExpiration<T>>(values.Count, StringComparer.Ordinal);

            foreach (var (key, value) in values)
            {
                if (!value.HasValue)
                {
                    continue;
                }

                var expiration = await _cache.GetExpirationAsync(key, ct).ConfigureAwait(false);
                result[key] = new CacheValueWithExpiration<T>(value, expiration);
            }

            return result;
        }

        public ValueTask<IDictionary<string, CacheValue<T>>> GetByPrefixAsync<T>(
            string prefix,
            CancellationToken ct = default
        ) => _cache.GetByPrefixAsync<T>(prefix, ct);

        public ValueTask<IReadOnlyList<string>> GetAllKeysByPrefixAsync(
            string prefix,
            CancellationToken ct = default
        ) => _cache.GetAllKeysByPrefixAsync(prefix, ct);

        public ValueTask<long> GetCountAsync(string prefix = "", CancellationToken ct = default) =>
            _cache.GetCountAsync(prefix, ct);

        public ValueTask<bool> ExistsAsync(string key, CancellationToken ct = default) => _cache.ExistsAsync(key, ct);

        public ValueTask<TimeSpan?> GetExpirationAsync(string key, CancellationToken ct = default) =>
            _cache.GetExpirationAsync(key, ct);

        public ValueTask<CacheValue<ICollection<T>>> GetSetAsync<T>(
            string key,
            int? pageIndex = null,
            int pageSize = 100,
            CancellationToken ct = default
        ) => _cache.GetSetAsync<T>(key, pageIndex, pageSize, ct);

        public ValueTask RefreshAsync(string key, CancellationToken ct = default) => _cache.RefreshAsync(key, ct);

        public ValueTask<bool> RemoveAsync(string key, CancellationToken ct = default) => _cache.RemoveAsync(key, ct);

        public ValueTask<bool> ExpireAsync(string key, CancellationToken ct = default) => _cache.ExpireAsync(key, ct);

        public ValueTask<bool> RemoveIfEqualAsync<T>(string key, T? expected, CancellationToken ct = default) =>
            _cache.RemoveIfEqualAsync(key, expected, ct);

        public ValueTask<int> RemoveAllAsync(IEnumerable<string> keys, CancellationToken ct = default) =>
            _cache.RemoveAllAsync(keys, ct);

        public ValueTask<int> RemoveByPrefixAsync(string prefix, CancellationToken ct = default) =>
            _cache.RemoveByPrefixAsync(prefix, ct);

        public ValueTask RemoveByTagAsync(string tag, CancellationToken ct = default) =>
            _cache.RemoveByTagAsync(tag, ct);

        public ValueTask ClearAsync(CancellationToken ct = default) => _cache.ClearAsync(ct);

        public ValueTask<long> SetRemoveAsync<T>(
            string key,
            IEnumerable<T> value,
            TimeSpan? expiration,
            CancellationToken ct = default
        ) => throw new InvalidOperationException("L2 write failed");

        public ValueTask FlushAsync(CancellationToken ct = default) => _cache.FlushAsync(ct);

        public void Dispose()
        {
            _cache.Dispose();
        }
    }

    [Fact]
    public async Task should_not_throw_when_publish_invalidation_fails()
    {
        // given
        var l1Options = new InMemoryCacheOptions { CloneValues = true };
        using var l1 = new InMemoryCache(_timeProvider, l1Options);
        var l2Options = new InMemoryCacheOptions { CloneValues = true };
        using var l2Base = new InMemoryCache(_timeProvider, l2Options);
        var l2 = new InMemoryRemoteCacheAdapter(l2Base);

        var publisher = Substitute.For<IBus>();
        publisher
            .PublishAsync(Arg.Any<CacheInvalidationMessage>(), Arg.Any<PublishOptions?>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("Publish failed"));

        var cache = new HybridCache(l1, l2, publisher, new HybridCacheOptions(), timeProvider: _timeProvider);
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        await l1.UpsertAsync(key, 42, TimeSpan.FromMinutes(5), AbortToken);
        await l2.UpsertAsync(key, 42, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var act = async () => await cache.RemoveAsync(key, AbortToken);

        // then - should not throw
        await act.Should().NotThrowAsync();

        // and the removal should still succeed locally
        var l1Result = await l1.GetAsync<int>(key, AbortToken);
        l1Result.HasValue.Should().BeFalse();
    }

    #endregion

    #region UpsertAsync

    [Fact]
    public async Task should_upsert_to_both_caches_and_publish_invalidation()
    {
        // given
        var (cache, l1, l2, publisher) = _CreateCache();
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        var value = Faker.Random.Int();

        // when
        var result = await cache.UpsertAsync(key, value, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().BeTrue();

        var l1Result = await l1.GetAsync<int>(key, AbortToken);
        l1Result.Value.Should().Be(value);

        var l2Result = await l2.GetAsync<int>(key, AbortToken);
        l2Result.Value.Should().Be(value);

        await publisher
            .Received(1)
            .PublishAsync(
                Arg.Is<CacheInvalidationMessage>(m => m.Key == key),
                Arg.Is<PublishOptions?>(options => options == null),
                Arg.Any<CancellationToken>()
            );
    }

    #endregion

    #region Numeric Operations

    [Fact]
    public async Task should_seed_l1_with_zero_when_increment_total_reaches_zero()
    {
        // given — a decrement that brings the running total back to exactly 0 must keep 0 in L1, not evict it.
        // Guards against treating the increment's new total of 0 as a "no-op" (the SetIfHigher/Lower difference
        // convention) and removing the key from L1.
        var (cache, l1, _, _) = _CreateCache();
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        await cache.IncrementAsync(key, 5L, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.IncrementAsync(key, -5L, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(0L);

        var l1Result = await l1.GetAsync<long>(key, AbortToken);
        l1Result.HasValue.Should().BeTrue("a 0 total is a valid stored value, not a no-op signal");
        l1Result.Value.Should().Be(0L);
    }

    #endregion

    #region Dispose

    [Fact]
    public async Task should_throw_when_used_after_dispose()
    {
        // given
        var (cache, _, _, _) = _CreateCache();

        await cache.DisposeAsync();

        // when
        var act = async () => await cache.GetAsync<int>("key", AbortToken);

        // then
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task should_handle_double_dispose_safely()
    {
        // given
        var (cache, _, _, _) = _CreateCache();

        // when
        var act = async () =>
        {
            await cache.DisposeAsync();
            await cache.DisposeAsync();
        };

        // then
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region Cancellation Token Propagation

    [Fact]
    public async Task should_cancel_factory_token_when_caller_token_is_cancelled()
    {
        // given — the coordinator hands the factory a detached token linked to caller cancellation,
        // not the caller token itself, so it can also cancel on a hard timeout.
        var (cache, _, _, _) = _CreateCache();
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        using var cts = new CancellationTokenSource();
        var receivedToken = CancellationToken.None;
        var factoryReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // when — cancel the caller token while the factory is in flight
        var act = async () =>
            await cache.GetOrAddAsync(
                key,
                async ct =>
                {
                    receivedToken = ct;
                    factoryReached.SetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                    return 42;
                },
                TimeSpan.FromMinutes(5),
                cts.Token
            );

        var assertion = act.Should().ThrowAsync<OperationCanceledException>();
        await factoryReached.Task;
        await cts.CancelAsync();
        await assertion;

        // then — the factory observed a cancellable token that fired on caller cancellation
        receivedToken.CanBeCanceled.Should().BeTrue();
        receivedToken.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task should_throw_when_cancellation_requested_before_operation()
    {
        // given
        var (cache, _, _, _) = _CreateCache();
        await using var _ = cache;

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // when
        var act = async () =>
            await cache.GetOrAddAsync("key", _ => new ValueTask<int?>(42), TimeSpan.FromMinutes(5), cts.Token);

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_throw_when_cancellation_requested_during_factory()
    {
        // given
        var (cache, _, _, _) = _CreateCache();
        await using var _ = cache;

        using var cts = new CancellationTokenSource();

        // when
        var act = async () =>
            await cache.GetOrAddAsync(
                "key",
                async ct =>
                {
                    await cts.CancelAsync();
                    ct.ThrowIfCancellationRequested();
                    return 42;
                },
                TimeSpan.FromMinutes(5),
                cts.Token
            );

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    #endregion

    #region Factory Write Backplane Publication

    [Fact]
    public async Task should_publish_key_invalidation_when_cold_miss_factory_writes()
    {
        // given
        var (cache, _, _, publisher) = _CreateCache();
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        var value = Faker.Random.Int();

        // when — cold miss runs the factory and writes both tiers
        var result = await cache.GetOrAddAsync(
            key,
            _ => new ValueTask<int?>(value),
            TimeSpan.FromMinutes(5),
            AbortToken
        );

        // then — the factory write broadcasts a key invalidation like the explicit upsert path
        result.Value.Should().Be(value);
        await publisher
            .Received(1)
            .PublishAsync(
                Arg.Is<CacheInvalidationMessage>(m => m.Key == key && m.InstanceId != null),
                Arg.Any<PublishOptions?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_publish_key_invalidation_when_soft_timeout_background_completion_writes()
    {
        // given — a stale reserve exists so the slow factory soft-times-out and completes in the background
        var (cache, _, _, publisher) = _CreateCache();
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        var options = new CacheEntryOptions
        {
            Duration = TimeSpan.FromMinutes(1),
            IsFailSafeEnabled = true,
            FailSafeMaxDuration = TimeSpan.FromHours(1),
            FailSafeThrottleDuration = TimeSpan.FromSeconds(30),
            FactorySoftTimeout = TimeSpan.FromMilliseconds(75),
            // Required now: fail-safe + finite soft timeout needs a finite ceiling (lock-hold guard).
            BackgroundFactoryCeiling = TimeSpan.FromSeconds(5),
        };

        await cache.GetOrAddAsync(key, _ => new ValueTask<int?>(1), options, AbortToken);
        _timeProvider.Advance(options.Duration + TimeSpan.FromMilliseconds(50));
        publisher.ClearReceivedCalls();

        var factoryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factoryGate = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);

        async ValueTask<int?> factory(CancellationToken cancellationToken)
        {
            factoryStarted.SetResult();
            return await factoryGate.Task.WaitAsync(cancellationToken);
        }

        // when — the caller soft-times-out, then the detached factory completes and writes through the store
        var timedOutTask = cache.GetOrAddAsync(key, factory, options, AbortToken).AsTask();
        await factoryStarted.Task;
        await _TriggerTimeoutAsync(options.FactorySoftTimeout, timedOutTask);
        var timedOut = await timedOutTask;
        factoryGate.SetResult(2);

        await _WaitUntilAsync(async () =>
        {
            var cached = await cache.GetAsync<int>(key, AbortToken);
            return cached.HasValue && cached.Value == 2;
        });

        // then — the background completion write published exactly one key invalidation
        timedOut.Value.Should().Be(1);
        timedOut.IsStale.Should().BeTrue();
        await publisher
            .Received(1)
            .PublishAsync(
                Arg.Is<CacheInvalidationMessage>(m => m.Key == key),
                Arg.Any<PublishOptions?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_publish_key_invalidation_when_eager_refresh_writes()
    {
        // given
        var (cache, _, _, publisher) = _CreateCache();
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        var options = new CacheEntryOptions { Duration = TimeSpan.FromMinutes(10), EagerRefreshThreshold = 0.5f };

        await cache.GetOrAddAsync(key, _ => new ValueTask<int?>(1), options, AbortToken);
        publisher.ClearReceivedCalls();
        _timeProvider.Advance(TimeSpan.FromMinutes(6)); // past the eager point, well before expiration

        // when — the fresh hit triggers a background eager refresh that writes the new value
        var hit = await cache.GetOrAddAsync(key, _ => new ValueTask<int?>(2), options, AbortToken);

        await _WaitUntilAsync(async () =>
        {
            var cached = await cache.GetAsync<int>(key, AbortToken);
            return cached.HasValue && cached.Value == 2;
        });

        // then — exactly one publish: the eager value write broadcasts, the eager gate restamp does not
        hit.Value.Should().Be(1);
        await publisher
            .Received(1)
            .PublishAsync(
                Arg.Is<CacheInvalidationMessage>(m => m.Key == key),
                Arg.Any<PublishOptions?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_not_publish_when_conditional_factory_reports_not_modified()
    {
        // given — a logically-expired but physically-present entry written by a conditional factory
        var (cache, _, _, publisher) = _CreateCache();
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        var value = Faker.Random.Int(1, 100);
        var options = new CacheEntryOptions
        {
            Duration = TimeSpan.FromMinutes(5),
            IsFailSafeEnabled = true,
            FailSafeMaxDuration = TimeSpan.FromHours(1),
        };

        await cache.GetOrAddAsync<int>(
            key,
            (context, _) => new ValueTask<CacheFactoryResult<int>>(context.Modified(value)),
            options,
            AbortToken
        );

        _timeProvider.Advance(options.Duration + TimeSpan.FromSeconds(1));
        publisher.ClearReceivedCalls();

        // when — the conditional factory extends the existing value in place (HTTP-304 pattern)
        var result = await cache.GetOrAddAsync<int>(
            key,
            (context, _) => new ValueTask<CacheFactoryResult<int>>(context.NotModified()),
            options,
            AbortToken
        );

        // then — peers' bytes are still identical, so no invalidation is broadcast for the restamp
        result.Value.Should().Be(value);
        result.IsStale.Should().BeFalse("the NotModified restamp makes the entry fresh again");
        await publisher
            .DidNotReceive()
            .PublishAsync(
                Arg.Any<CacheInvalidationMessage>(),
                Arg.Any<PublishOptions?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_not_fail_caller_and_queue_publish_for_replay_when_factory_write_publish_fails()
    {
        // given — L2 healthy, the backplane down for the first publish, auto-recovery on
        var failPublish = true;
        var publisher = Substitute.For<IBus>();

        publisher
            .PublishAsync(Arg.Any<CacheInvalidationMessage>(), Arg.Any<PublishOptions?>(), Arg.Any<CancellationToken>())
            .Returns(_ => failPublish ? throw new InvalidOperationException("Publish failed") : Task.CompletedTask);

        using var l1 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions { CloneValues = true });
        using var l2Inner = new InMemoryCache(_timeProvider, new InMemoryCacheOptions { CloneValues = true });
        var l2 = new InMemoryRemoteCacheAdapter(l2Inner);

        await using var cache = new HybridCache(
            l1,
            l2,
            publisher,
            new HybridCacheOptions { EnableAutoRecovery = true },
            timeProvider: _timeProvider
        );

        var key = Faker.Random.AlphaNumeric(10);
        var value = Faker.Random.Int();

        // when — the factory write succeeds but its invalidation publish fails
        var result = await cache.GetOrAddAsync(
            key,
            _ => new ValueTask<int?>(value),
            TimeSpan.FromMinutes(5),
            AbortToken
        );

        // then — the caller is unaffected, both tiers hold the value, and the publish is queued for replay
        result.Value.Should().Be(value);
        (await l2.GetAsync<int>(key, AbortToken)).Value.Should().Be(value);
        cache.RecoveryQueue!.Count.Should().Be(1);

        // when — the backplane recovers and the cadence elapses
        failPublish = false;
        _timeProvider.Advance(TimeSpan.FromSeconds(5));
        await cache.RecoveryQueue.ProcessAsync(AbortToken);

        // then — the captured invalidation was re-published
        cache.RecoveryQueue.Count.Should().Be(0);
        await publisher
            .Received(2)
            .PublishAsync(
                Arg.Is<CacheInvalidationMessage>(m => m.Key == key),
                Arg.Any<PublishOptions?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_drop_peer_l1_entry_when_factory_write_invalidation_received()
    {
        // given — two nodes share an L2; the peer's L1 holds an outdated copy of the key
        using var sharedL2Base = new InMemoryCache(_timeProvider, new InMemoryCacheOptions { CloneValues = true });

        var sharedL2 = new InMemoryRemoteCacheAdapter(sharedL2Base);

        var published = new List<CacheInvalidationMessage>();
        var publisherA = Substitute.For<IBus>();
        publisherA
            .PublishAsync(
                Arg.Do<CacheInvalidationMessage>(published.Add),
                Arg.Any<PublishOptions?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.CompletedTask);

        var publisherB = Substitute.For<IBus>();
        publisherB
            .PublishAsync(Arg.Any<CacheInvalidationMessage>(), Arg.Any<PublishOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        using var l1A = new InMemoryCache(_timeProvider, new InMemoryCacheOptions { CloneValues = true });
        using var l1B = new InMemoryCache(_timeProvider, new InMemoryCacheOptions { CloneValues = true });

        var nodeA = new HybridCache(
            l1A,
            sharedL2,
            publisherA,
            new HybridCacheOptions { InstanceId = "node-a" },
            timeProvider: _timeProvider
        );
        await using var _ = nodeA;

        var nodeB = new HybridCache(
            l1B,
            sharedL2,
            publisherB,
            new HybridCacheOptions { InstanceId = "node-b" },
            timeProvider: _timeProvider
        );
        await using var __ = nodeB;

        var key = Faker.Random.AlphaNumeric(10);
        await l1B.UpsertAsync(key, 1, TimeSpan.FromMinutes(5), AbortToken);

        // when — node A's factory write publishes, and node B receives the invalidation
        var fresh = await nodeA.GetOrAddAsync(key, _ => new ValueTask<int?>(2), TimeSpan.FromMinutes(5), AbortToken);
        published.Should().ContainSingle(m => m.Key == key && m.InstanceId == "node-a");
        await nodeB.HandleInvalidationAsync(
            published.Single(m => string.Equals(m.Key, key, StringComparison.Ordinal)),
            AbortToken
        );

        // then — node B dropped its outdated L1 copy and the next read converges through the shared L2
        fresh.Value.Should().Be(2);
        (await l1B.GetAsync<int>(key, AbortToken)).HasValue.Should().BeFalse();
        (await nodeB.GetAsync<int>(key, AbortToken)).Value.Should().Be(2);
    }

    [Fact]
    public async Task should_ignore_self_originated_factory_write_invalidation()
    {
        // given — a node that captures its own factory-write invalidation
        var published = new List<CacheInvalidationMessage>();
        var publisher = Substitute.For<IBus>();

        publisher
            .PublishAsync(
                Arg.Do<CacheInvalidationMessage>(published.Add),
                Arg.Any<PublishOptions?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.CompletedTask);

        using var l1 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions { CloneValues = true });
        using var l2Inner = new InMemoryCache(_timeProvider, new InMemoryCacheOptions { CloneValues = true });
        var l2 = new InMemoryRemoteCacheAdapter(l2Inner);

        await using var cache = new HybridCache(
            l1,
            l2,
            publisher,
            new HybridCacheOptions { InstanceId = "instance-1" },
            timeProvider: _timeProvider
        );

        var key = Faker.Random.AlphaNumeric(10);
        var value = Faker.Random.Int();
        await cache.GetOrAddAsync(key, _ => new ValueTask<int?>(value), TimeSpan.FromMinutes(5), AbortToken);
        published.Should().ContainSingle(m => m.Key == key);

        // when — the message echoes back to its own originator
        await cache.HandleInvalidationAsync(published.Single(), AbortToken);

        // then — the self-originated message is filtered and the freshly-written L1 entry survives
        (await l1.GetAsync<int>(key, AbortToken))
            .Value.Should()
            .Be(value);
        cache.InvalidateCacheCalls.Should().Be(0);
    }

    #endregion

    #region L2 Disruption Mid-Factory

    [Fact]
    public async Task should_win_with_factory_value_when_l2_entry_removed_mid_factory()
    {
        // given — a factory in flight while another actor writes and then removes the L2 entry
        var (cache, l1, l2, _) = _CreateCache();
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        var factoryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factoryGate = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);

        async ValueTask<int?> factory(CancellationToken cancellationToken)
        {
            factoryStarted.SetResult();
            return await factoryGate.Task.WaitAsync(cancellationToken);
        }

        var pending = cache.GetOrAddAsync(key, factory, TimeSpan.FromMinutes(5), AbortToken).AsTask();
        await factoryStarted.Task;

        // when — the L2 entry is created and removed by another actor mid-operation, then the factory lands
        await l2.UpsertAsync(key, 99, TimeSpan.FromMinutes(5), AbortToken);
        await l2.RemoveAsync(key, AbortToken);
        factoryGate.SetResult(42);
        var result = await pending;

        // then — the factory result wins and both tiers converge on it (no torn state)
        result.Value.Should().Be(42);
        (await l1.GetAsync<int>(key, AbortToken)).Value.Should().Be(42);
        (await l2.GetAsync<int>(key, AbortToken)).Value.Should().Be(42);
    }

    [Fact]
    public async Task should_return_factory_value_and_keep_l1_when_l2_starts_failing_mid_factory()
    {
        // given — L2 healthy at read time, failing by the time the factory write lands
        using var l2 = new TogglableRemoteCache(_timeProvider);
        using var l1 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions { CloneValues = true });
        var publisher = Substitute.For<IBus>();
        publisher
            .PublishAsync(Arg.Any<CacheInvalidationMessage>(), Arg.Any<PublishOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var cache = new HybridCache(l1, l2, publisher, new HybridCacheOptions(), timeProvider: _timeProvider);
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        var factoryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factoryGate = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);

        async ValueTask<int?> factory(CancellationToken cancellationToken)
        {
            factoryStarted.SetResult();
            return await factoryGate.Task.WaitAsync(cancellationToken);
        }

        var pending = cache.GetOrAddAsync(key, factory, TimeSpan.FromMinutes(5), AbortToken).AsTask();
        await factoryStarted.Task;

        // when — L2 starts throwing mid-operation, then the factory completes
        l2.FailWrites = true;
        factoryGate.SetResult(42);
        var result = await pending;

        // then — the caller still gets the factory value, L1 holds it, the failed L2 write is swallowed
        // (L2 stays empty until a later write or auto-recovery), and the invalidation is still broadcast
        result.Value.Should().Be(42);
        (await l1.GetAsync<int>(key, AbortToken)).Value.Should().Be(42);
        (await l2.GetAsync<int>(key, AbortToken)).HasValue.Should().BeFalse("the L2 write failed and was swallowed");
        await publisher
            .Received(1)
            .PublishAsync(
                Arg.Is<CacheInvalidationMessage>(m => m.Key == key),
                Arg.Any<PublishOptions?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_wipe_l1_when_l2_remove_fails_in_breaker_only_mode()
    {
        // given — circuit breaker on, auto-recovery off (RecoveryQueue null). An L2 RemoveAsync failure must not
        // leave this node's L1 serving the value the caller asked to remove. (re-review N1)
        using var l2 = new TogglableRemoteCache(_timeProvider);
        var l1 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions { CloneValues = true });
        var publisher = Substitute.For<IBus>();
        publisher
            .PublishAsync(Arg.Any<CacheInvalidationMessage>(), Arg.Any<PublishOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var cache = new HybridCache(
            l1,
            l2,
            publisher,
            new HybridCacheOptions { DistributedCacheCircuitBreakerDuration = TimeSpan.FromSeconds(30) },
            timeProvider: _timeProvider
        );
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        await cache.UpsertAsync(key, 7, TimeSpan.FromMinutes(5), AbortToken);
        (await l1.GetAsync<int>(key, AbortToken)).HasValue.Should().BeTrue();

        // when — the L2 remove throws
        l2.FailWrites = true;
        Func<Task> act = () => cache.RemoveAsync(key, AbortToken).AsTask();

        // then — the failure surfaces, but L1 no longer serves the removed value
        await act.Should().ThrowAsync<InvalidOperationException>();
        (await l1.GetAsync<int>(key, AbortToken))
            .HasValue.Should()
            .BeFalse("L1 must be wiped before the L2 failure is rethrown");
    }

    [Fact]
    public async Task should_wipe_l1_when_l2_try_replace_if_equal_fails_in_breaker_only_mode()
    {
        // given — an L2 CAS failure leaves the outcome unknown (the write may have landed before the fault), so
        // this node's L1 must stop serving the pre-replace value, mirroring RemoveIfEqualAsync. (review #1)
        var (cache, l1, l2) = _CreateBreakerOnlyCache();
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        await cache.UpsertAsync(key, 7, TimeSpan.FromMinutes(5), AbortToken);
        (await l1.GetAsync<int>(key, AbortToken)).HasValue.Should().BeTrue();

        l2.FailWrites = true;
        Func<Task> act = () => cache.TryReplaceIfEqualAsync(key, 7, 8, TimeSpan.FromMinutes(5), AbortToken).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await l1.GetAsync<int>(key, AbortToken))
            .HasValue.Should()
            .BeFalse("L1 must be wiped before the L2 CAS failure is rethrown");
    }

    [Fact]
    public async Task should_wipe_l1_when_l2_increment_fails_in_breaker_only_mode()
    {
        // given — a failed L2 numeric op has an unknown outcome; L1's copy may diverge from L2, so it is wiped
        // and re-seeded from L2 on the next read. (review #1)
        var (cache, l1, l2) = _CreateBreakerOnlyCache();
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        await cache.IncrementAsync(key, 7L, TimeSpan.FromMinutes(5), AbortToken);
        (await l1.GetAsync<long>(key, AbortToken)).HasValue.Should().BeTrue();

        l2.FailWrites = true;
        Func<Task> act = () => cache.IncrementAsync(key, 1L, TimeSpan.FromMinutes(5), AbortToken).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await l1.GetAsync<long>(key, AbortToken))
            .HasValue.Should()
            .BeFalse("L1 must be wiped before the L2 numeric-op failure is rethrown");
    }

    [Fact]
    public async Task should_wipe_l1_when_l2_set_add_fails_in_breaker_only_mode()
    {
        // given — a failed L2 set mutation has an unknown outcome; the L1 set copy may diverge from L2. (review #1)
        var (cache, l1, l2) = _CreateBreakerOnlyCache();
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        await cache.SetAddAsync(key, new[] { "a", "b" }, TimeSpan.FromMinutes(5), AbortToken);
        (await l1.GetSetAsync<string>(key, cancellationToken: AbortToken)).HasValue.Should().BeTrue();

        l2.FailWrites = true;
        Func<Task> act = () => cache.SetAddAsync(key, new[] { "c" }, TimeSpan.FromMinutes(5), AbortToken).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await l1.GetSetAsync<string>(key, cancellationToken: AbortToken))
            .HasValue.Should()
            .BeFalse("L1 must be wiped before the L2 set-mutation failure is rethrown");
    }

    [Fact]
    public async Task should_wipe_l1_when_l2_set_remove_fails_in_breaker_only_mode()
    {
        // given — a failed L2 set mutation has an unknown outcome; the L1 set copy may diverge from L2. (review #1)
        var (cache, l1, l2) = _CreateBreakerOnlyCache();
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        await cache.SetAddAsync(key, new[] { "a", "b" }, TimeSpan.FromMinutes(5), AbortToken);
        (await l1.GetSetAsync<string>(key, cancellationToken: AbortToken)).HasValue.Should().BeTrue();

        l2.FailWrites = true;
        Func<Task> act = () => cache.SetRemoveAsync(key, new[] { "a" }, TimeSpan.FromMinutes(5), AbortToken).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await l1.GetSetAsync<string>(key, cancellationToken: AbortToken))
            .HasValue.Should()
            .BeFalse("L1 must be wiped before the L2 set-mutation failure is rethrown");
    }

    [Fact]
    public async Task should_wipe_l1_prefix_when_l2_remove_by_prefix_fails_in_breaker_only_mode()
    {
        // given — a failed L2 prefix sweep may have partially landed; this node must stop serving the matching
        // L1 entries the caller asked to delete, mirroring RemoveAllAsync's catch. (review #2)
        var (cache, l1, l2) = _CreateBreakerOnlyCache();
        await using var _ = cache;

        var prefix = Faker.Random.AlphaNumeric(10) + ":";
        var key = prefix + "1";
        await cache.UpsertAsync(key, 7, TimeSpan.FromMinutes(5), AbortToken);
        (await l1.GetAsync<int>(key, AbortToken)).HasValue.Should().BeTrue();

        l2.FailWrites = true;
        Func<Task> act = () => cache.RemoveByPrefixAsync(prefix, AbortToken).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await l1.GetAsync<int>(key, AbortToken))
            .HasValue.Should()
            .BeFalse("matching L1 entries must be wiped before the L2 prefix-sweep failure is rethrown");
    }

    private (HybridCache cache, InMemoryCache l1, TogglableRemoteCache l2) _CreateBreakerOnlyCache()
    {
        // Circuit breaker on, auto-recovery off (RecoveryQueue null) — the breaker-only degraded mode the
        // L1-consistency-on-L2-failure contract is defined for.
        var l2 = new TogglableRemoteCache(_timeProvider);
        var l1 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions { CloneValues = true });
        var publisher = Substitute.For<IBus>();
        publisher
            .PublishAsync(Arg.Any<CacheInvalidationMessage>(), Arg.Any<PublishOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var cache = new HybridCache(
            l1,
            l2,
            publisher,
            new HybridCacheOptions { DistributedCacheCircuitBreakerDuration = TimeSpan.FromSeconds(30) },
            timeProvider: _timeProvider
        );

        return (cache, l1, l2);
    }

    [Fact]
    public async Task should_resurrect_key_when_factory_write_lands_after_concurrent_remove()
    {
        // given — RemoveAsync does not take the per-key factory lock, so it can interleave with a factory
        var (cache, l1, l2, _) = _CreateCache();
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        var factoryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factoryGate = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);

        async ValueTask<int?> factory(CancellationToken cancellationToken)
        {
            factoryStarted.SetResult();
            return await factoryGate.Task.WaitAsync(cancellationToken);
        }

        var pending = cache.GetOrAddAsync(key, factory, TimeSpan.FromMinutes(5), AbortToken).AsTask();
        await factoryStarted.Task;

        // when — the key is removed while the factory is in flight, then the factory write lands
        await cache.RemoveAsync(key, AbortToken);
        factoryGate.SetResult(42);
        var result = await pending;

        // then — PINNED: the late factory write wins and resurrects the key in both tiers. The write is not
        // conditional on the entry still existing, so a concurrent remove is overwritten by the in-flight factory.
        result.Value.Should().Be(42);
        (await l1.GetAsync<int>(key, AbortToken)).Value.Should().Be(42);
        (await l2.GetAsync<int>(key, AbortToken)).Value.Should().Be(42);
    }

    [Fact]
    public async Task should_resurrect_key_when_factory_write_lands_after_concurrent_invalidation_message()
    {
        // given — a foreign invalidation arrives while a factory for the same key is in flight
        var options = new HybridCacheOptions { InstanceId = "instance-1" };
        var (cache, l1, l2, _) = _CreateCache(options);
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        var factoryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factoryGate = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);

        async ValueTask<int?> factory(CancellationToken cancellationToken)
        {
            factoryStarted.SetResult();
            return await factoryGate.Task.WaitAsync(cancellationToken);
        }

        var pending = cache.GetOrAddAsync(key, factory, TimeSpan.FromMinutes(5), AbortToken).AsTask();
        await factoryStarted.Task;

        // when — the invalidation-driven removal fires mid-factory, then the factory write lands
        await cache.HandleInvalidationAsync(
            new CacheInvalidationMessage
            {
                InstanceId = "instance-2",
                Key = key,
                Timestamp = _timeProvider.GetUtcNow(),
            },
            AbortToken
        );
        factoryGate.SetResult(42);
        var result = await pending;

        // then — PINNED: the late factory write resurrects the key; an invalidation racing an in-flight
        // factory does not suppress the subsequent write.
        result.Value.Should().Be(42);
        cache.InvalidateCacheCalls.Should().Be(1);
        (await l1.GetAsync<int>(key, AbortToken)).Value.Should().Be(42);
        (await l2.GetAsync<int>(key, AbortToken)).Value.Should().Be(42);
    }

    #endregion

    #region Batch Operations Under Partial L2 Failure

    [Fact]
    public async Task should_return_partial_count_clear_l1_members_and_publish_all_keys_when_upsert_all_partially_succeeds()
    {
        // given — L2 reports a partial batch write (2 of 3 members landed)
        using var l1 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions { CloneValues = true });
        var l2 = Substitute.For<IRemoteCache>();
        l2.UpsertAllAsync(Arg.Any<IDictionary<string, int>>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(2);

        var publisher = Substitute.For<IBus>();
        publisher
            .PublishAsync(Arg.Any<CacheInvalidationMessage>(), Arg.Any<PublishOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var cache = new HybridCache(
            l1,
            l2,
            publisher,
            new HybridCacheOptions { EnableAutoRecovery = true },
            timeProvider: _timeProvider
        );
        await using var _ = cache;

        var values = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["batch-key-1"] = 1,
            ["batch-key-2"] = 2,
            ["batch-key-3"] = 3,
        };

        foreach (var (key, value) in values)
        {
            await l1.UpsertAsync(key, value + 100, TimeSpan.FromMinutes(5), AbortToken); // outdated L1 copies
        }

        // when
        var setCount = await cache.UpsertAllAsync(values, TimeSpan.FromMinutes(5), AbortToken);

        // then — PINNED: the L2 count is returned as-is; ALL members (including the succeeded ones) are
        // conservatively dropped from L1 because the failed subset is unknown; the invalidation is still
        // published for all keys; and nothing is queued (bulk operations are never captured by auto-recovery).
        setCount.Should().Be(2);

        foreach (var key in values.Keys)
        {
            (await l1.GetAsync<int>(key, AbortToken)).HasValue.Should().BeFalse("partial batch failure clears L1");
        }

        await publisher
            .Received(1)
            .PublishAsync(
                Arg.Is<CacheInvalidationMessage>(m => m.Keys != null && m.Keys.Length == 3),
                Arg.Any<PublishOptions?>(),
                Arg.Any<CancellationToken>()
            );
        cache.RecoveryQueue!.Count.Should().Be(0);
    }

    [Fact]
    public async Task should_propagate_exception_and_leave_l1_untouched_when_remove_all_l2_fails_mid_batch()
    {
        // given — the L2 bulk removal throws
        using var l1 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions { CloneValues = true });
        var l2 = Substitute.For<IRemoteCache>();
        l2.RemoveAllAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns<int>(_ => throw new InvalidOperationException("L2 batch failed"));

        var publisher = Substitute.For<IBus>();
        publisher
            .PublishAsync(Arg.Any<CacheInvalidationMessage>(), Arg.Any<PublishOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var cache = new HybridCache(
            l1,
            l2,
            publisher,
            new HybridCacheOptions { EnableAutoRecovery = true },
            timeProvider: _timeProvider
        );
        await using var _ = cache;

        string[] keys = ["bulk-remove-1", "bulk-remove-2"];

        foreach (var key in keys)
        {
            await l1.UpsertAsync(key, 1, TimeSpan.FromMinutes(5), AbortToken);
        }

        // when
        var act = async () => await cache.RemoveAllAsync(keys, AbortToken);

        // then — the L2 failure propagates to the caller; L1 is cleaned before the rethrow to avoid leaving
        // stale entries on this node (finding #4 fix). Nothing is published because L2 never confirmed the
        // removal, and bulk removals are never captured by auto-recovery.
        await act.Should().ThrowAsync<InvalidOperationException>();

        foreach (var key in keys)
        {
            (await l1.GetAsync<int>(key, AbortToken))
                .HasValue.Should()
                .BeFalse("L1 is cleaned before the rethrow to avoid stale entries");
        }

        await publisher
            .DidNotReceive()
            .PublishAsync(
                Arg.Any<CacheInvalidationMessage>(),
                Arg.Any<PublishOptions?>(),
                Arg.Any<CancellationToken>()
            );
        cache.RecoveryQueue!.Count.Should().Be(0);
    }

    [Fact]
    public async Task should_degrade_to_partial_l1_result_when_get_all_l2_read_fails_mid_batch()
    {
        // given — L1 holds one of the two requested keys; the L2 batch read for the misses throws
        using var l1 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions { CloneValues = true });
        var l2 = Substitute.For<IRemoteCache>();
        l2.GetAllWithExpirationAsync<int>(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns<IDictionary<string, CacheValueWithExpiration<int>>>(_ =>
                throw new InvalidOperationException("L2 read failed")
            );

        var publisher = Substitute.For<IBus>();
        publisher
            .PublishAsync(Arg.Any<CacheInvalidationMessage>(), Arg.Any<PublishOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var cache = new HybridCache(
            l1,
            l2,
            publisher,
            new HybridCacheOptions { EnableAutoRecovery = true },
            timeProvider: _timeProvider
        );
        await using var _ = cache;

        await l1.UpsertAsync("batch-get-1", 1, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.GetAllAsync<int>(["batch-get-1", "batch-get-2"], AbortToken);

        // then — L2 read failure degrades to the partial L1 result (mirrors single-key GetAsync contract):
        // the L1 hit is returned, the L2 miss key comes back as NoValue, and nothing is queued.
        result.Should().ContainKey("batch-get-1");
        result["batch-get-1"].HasValue.Should().BeTrue();
        result["batch-get-1"].Value.Should().Be(1);
        result.Should().ContainKey("batch-get-2");
        result["batch-get-2"].HasValue.Should().BeFalse("L2 failed before producing a value for this key");
        cache.RecoveryQueue!.Count.Should().Be(0);
    }

    [Fact]
    public async Task should_degrade_to_partial_l1_result_when_get_all_l2_read_times_out()
    {
        // given — L1 holds one of the two requested keys; the L2 batch read parks indefinitely (soft timeout fires)
        var timeProvider = TimeProvider.System;
        var softTimeout = TimeSpan.FromMilliseconds(50);
        using var l1 = new InMemoryCache(timeProvider, new InMemoryCacheOptions { CloneValues = true });
        using var l2 = new GatedRemoteCache(timeProvider)
        {
            ReadGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        var publisher = Substitute.For<IBus>();
        publisher
            .PublishAsync(Arg.Any<CacheInvalidationMessage>(), Arg.Any<PublishOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var cache = new HybridCache(
            l1,
            l2,
            publisher,
            new HybridCacheOptions
            {
                DistributedCacheSoftTimeout = softTimeout,
                DistributedCacheHardTimeout = TimeSpan.FromSeconds(5),
            },
            timeProvider: timeProvider
        );
        await using var __ = cache;

        await l1.UpsertAsync("batch-get-1", 1, TimeSpan.FromMinutes(5), AbortToken);

        // when — the soft timeout fires while the L2 read is parked
        var task = cache.GetAllAsync<int>(["batch-get-1", "batch-get-2"], AbortToken).AsTask();
        await l2.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        var result = await task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // then — degraded to partial L1 result without throwing; L2 miss key is NoValue
        result.Should().ContainKey("batch-get-1");
        result["batch-get-1"].HasValue.Should().BeTrue();
        result["batch-get-1"].Value.Should().Be(1);
        result.Should().ContainKey("batch-get-2");
        result["batch-get-2"].HasValue.Should().BeFalse("L2 timed out before returning a value for this key");
        l2.ReadAttempts.Should().Be(1);
    }

    [Fact]
    public async Task should_log_bulk_read_degraded_without_exception_when_get_all_l2_read_times_out()
    {
        // given — L1 holds one of the two requested keys; the L2 batch read parks indefinitely so the soft timeout
        // fires. This drives the no-exception degrade arm specifically (LogBulkDistributedCacheReadDegraded), as
        // opposed to the exception arm (LogFailedBulkL2CacheOperation) covered by the mid-batch-throw test above.
        var timeProvider = TimeProvider.System;
        var softTimeout = TimeSpan.FromMilliseconds(50);
        using var l1 = new InMemoryCache(timeProvider, new InMemoryCacheOptions { CloneValues = true });
        using var l2 = new GatedRemoteCache(timeProvider)
        {
            ReadGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        var publisher = Substitute.For<IBus>();
        publisher
            .PublishAsync(Arg.Any<CacheInvalidationMessage>(), Arg.Any<PublishOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var logger = new RecordingHybridCacheLogger();
        var cache = new HybridCache(
            l1,
            l2,
            publisher,
            new HybridCacheOptions
            {
                DistributedCacheSoftTimeout = softTimeout,
                DistributedCacheHardTimeout = TimeSpan.FromSeconds(5),
            },
            logger: logger,
            timeProvider: timeProvider
        );
        await using var __ = cache;

        await l1.UpsertAsync("batch-get-1", 1, TimeSpan.FromMinutes(5), AbortToken);

        // when — the soft timeout fires while the L2 read is parked
        var task = cache.GetAllAsync<int>(["batch-get-1", "batch-get-2"], AbortToken).AsTask();
        await l2.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        var result = await task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // then — degraded to the partial L1 result without throwing: the L1 hit is present, the L2-missed key absent
        result["batch-get-1"].HasValue.Should().BeTrue();
        result["batch-get-1"].Value.Should().Be(1);
        result["batch-get-2"].HasValue.Should().BeFalse("L2 timed out before returning a value for this key");

        // and — the status-log (no-exception) degrade arm fired (EventId 22), not the exception arm (EventId 21)
        logger
            .Entries.Should()
            .Contain(e => e.Event.Id == 22, "the timeout/circuit-open arm logs without an exception");
        logger.Entries.Should().NotContain(e => e.Event.Id == 21, "no exception arm should fire on a clean timeout");
    }

    #endregion

    #region Cancellation During L2 Phases

    [Fact]
    public async Task should_propagate_cancellation_and_not_run_factory_when_caller_cancels_mid_l2_read()
    {
        // given — the L2 read parks on a gate that honors the caller token
        using var l2 = new GatedRemoteCache(_timeProvider)
        {
            ReadGate = new(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        using var l1 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions { CloneValues = true });
        var publisher = Substitute.For<IBus>();
        publisher
            .PublishAsync(Arg.Any<CacheInvalidationMessage>(), Arg.Any<PublishOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await using var cache = new HybridCache(
            l1,
            l2,
            publisher,
            new HybridCacheOptions { EnableAutoRecovery = true },
            timeProvider: _timeProvider
        );

        var key = Faker.Random.AlphaNumeric(10);
        var factoryCalls = 0;
        using var cts = new CancellationTokenSource();

        // when — the caller cancels while the L2 read is in flight
        var pending = cache
            .GetOrAddAsync(
                key,
                _ =>
                {
                    factoryCalls++;
                    return new ValueTask<int?>(42);
                },
                TimeSpan.FromMinutes(5),
                cts.Token
            )
            .AsTask();
        await l2.ReadStarted.Task;
        await cts.CancelAsync();
        var act = async () => await pending;

        // then — caller cancellation propagates instead of being swallowed as an L2 miss: the factory never
        // runs and nothing is misclassified into the recovery queue
        await act.Should().ThrowAsync<OperationCanceledException>();
        factoryCalls.Should().Be(0, "a cancelled L2 read must not degrade to a miss that runs the factory");
        cache.RecoveryQueue!.Count.Should().Be(0);
    }

    [Fact]
    public async Task should_propagate_cancellation_and_not_queue_recovery_when_caller_cancels_mid_l2_write()
    {
        // given — the factory completes instantly and the L2 write parks on a gate honoring the caller token
        using var l2 = new GatedRemoteCache(_timeProvider)
        {
            WriteGate = new(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        using var l1 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions { CloneValues = true });
        var publisher = Substitute.For<IBus>();
        publisher
            .PublishAsync(Arg.Any<CacheInvalidationMessage>(), Arg.Any<PublishOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var cache = new HybridCache(
            l1,
            l2,
            publisher,
            new HybridCacheOptions { EnableAutoRecovery = true },
            timeProvider: _timeProvider
        );
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        using var cts = new CancellationTokenSource();

        // when — the caller cancels while the factory result is being written to L2
        var pending = cache
            .GetOrAddAsync(key, _ => new ValueTask<int?>(42), TimeSpan.FromMinutes(5), cts.Token)
            .AsTask();
        await l2.WriteStarted.Task;
        await cts.CancelAsync();
        var act = async () => await pending;

        // then — caller cancellation propagates and is NOT misclassified as an L2 outage: nothing lands in the
        // recovery queue, the aborted write does not reach L1, and no invalidation is broadcast
        await act.Should().ThrowAsync<OperationCanceledException>();
        cache.RecoveryQueue!.Count.Should().Be(0, "caller cancellation must not be queued as a failed L2 write");
        (await l1.GetAsync<int>(key, AbortToken)).HasValue.Should().BeFalse();
        await publisher
            .DidNotReceive()
            .PublishAsync(
                Arg.Any<CacheInvalidationMessage>(),
                Arg.Any<PublishOptions?>(),
                Arg.Any<CancellationToken>()
            );
    }

    #endregion

    #region Test Helpers

    // The coordinator arms its delay timer only after the factory has started, so with a fake time provider a
    // single advance can land before the timer exists. Keep nudging time forward on a real-time cadence until
    // the caller-observable task completes (mirrors the conformance harness helper).
    private async ValueTask _TriggerTimeoutAsync(TimeSpan timeout, Task pendingTimeout)
    {
        await Task.Yield();
        _timeProvider.Advance(timeout);

        for (var attempt = 0; attempt < 50 && !pendingTimeout.IsCompleted; attempt++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), AbortToken);
            _timeProvider.Advance(TimeSpan.FromMilliseconds(20));
        }
    }

    private async ValueTask _WaitUntilAsync(Func<ValueTask<bool>> condition)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (await condition())
            {
                return;
            }

            _timeProvider.Advance(TimeSpan.FromMilliseconds(20));
            await Task.Delay(TimeSpan.FromMilliseconds(10), AbortToken);
        }

        throw new TimeoutException("Condition was not satisfied within the polling window.");
    }

    #endregion

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
}
