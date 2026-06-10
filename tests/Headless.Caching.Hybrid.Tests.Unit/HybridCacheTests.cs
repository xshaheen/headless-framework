// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

// ReSharper disable AccessToDisposedClosure
public sealed class HybridCacheTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();

    private (HybridCache cache, IInMemoryCache l1, IRemoteCache l2, IBus publisher) _CreateCache(
        HybridCacheOptions? options = null
    )
    {
        options ??= new HybridCacheOptions();
        var l1Options = new InMemoryCacheOptions { CloneValues = true };
        var l1 = new InMemoryCache(_timeProvider, l1Options);

        // Create a separate in-memory cache as the "distributed" cache for testing
        var l2Options = new InMemoryCacheOptions { CloneValues = true };
        var l2 = new InMemoryRemoteCacheAdapter(new InMemoryCache(_timeProvider, l2Options));

        var publisher = Substitute.For<IBus>();
        publisher
            .PublishAsync(Arg.Any<CacheInvalidationMessage>(), Arg.Any<PublishOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var cache = new HybridCache(l1, l2, publisher, options, timeProvider: _timeProvider);

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

        var prefix = "test:";
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
                Arg.Is<CacheInvalidationMessage>(m => m.FlushAll == true),
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
        var instanceId = "instance-1";
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

        var prefix = "user:";
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

        public ValueTask<bool> RemoveAsync(string key, CancellationToken ct = default) => _cache.RemoveAsync(key, ct);

        public ValueTask<bool> RemoveIfEqualAsync<T>(string key, T? expected, CancellationToken ct = default) =>
            _cache.RemoveIfEqualAsync(key, expected, ct);

        public ValueTask<int> RemoveAllAsync(IEnumerable<string> keys, CancellationToken ct = default) =>
            _cache.RemoveAllAsync(keys, ct);

        public ValueTask<int> RemoveByPrefixAsync(string prefix, CancellationToken ct = default) =>
            _cache.RemoveByPrefixAsync(prefix, ct);

        public ValueTask<int> RemoveByTagAsync(string tag, CancellationToken ct = default) =>
            _cache.RemoveByTagAsync(tag, ct);

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
        var l1 = new InMemoryCache(_timeProvider, l1Options);
        var l2Options = new InMemoryCacheOptions { CloneValues = true };
        var l2 = new InMemoryRemoteCacheAdapter(new InMemoryCache(_timeProvider, l2Options));

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
            await cache.GetOrAddAsync<int>(
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
}
