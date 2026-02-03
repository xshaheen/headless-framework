// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Caching;
using Framework.Testing.Tests;
using Xunit;

namespace Framework.Caching.Tests.Unit;

#pragma warning disable VSTHRD003 // Avoid awaiting foreign Tasks
public sealed class CacheExtensionsTests : TestBase
{
    /// <summary>
    /// Test wrapper that delegates to a mock while providing access to default interface methods.
    /// NSubstitute mocks don't include default interface implementations.
    /// </summary>
    private sealed class TestCache(ICache inner) : ICache
    {
        public Task<bool> UpsertAsync<T>(string key, T? value, TimeSpan? expiration, CancellationToken cancellationToken = default)
            => inner.UpsertAsync(key, value, expiration, cancellationToken);

        public Task<int> UpsertAllAsync<T>(IDictionary<string, T> value, TimeSpan? expiration, CancellationToken cancellationToken = default)
            => inner.UpsertAllAsync(value, expiration, cancellationToken);

        public Task<bool> TryInsertAsync<T>(string key, T? value, TimeSpan? expiration, CancellationToken cancellationToken = default)
            => inner.TryInsertAsync(key, value, expiration, cancellationToken);

        public Task<bool> TryReplaceAsync<T>(string key, T? value, TimeSpan? expiration, CancellationToken cancellationToken = default)
            => inner.TryReplaceAsync(key, value, expiration, cancellationToken);

        public Task<bool> TryReplaceIfEqualAsync<T>(string key, T? expected, T? value, TimeSpan? expiration, CancellationToken cancellationToken = default)
            => inner.TryReplaceIfEqualAsync(key, expected, value, expiration, cancellationToken);

        public Task<double> IncrementAsync(string key, double amount, TimeSpan? expiration, CancellationToken cancellationToken = default)
            => inner.IncrementAsync(key, amount, expiration, cancellationToken);

        public Task<long> IncrementAsync(string key, long amount, TimeSpan? expiration, CancellationToken cancellationToken = default)
            => inner.IncrementAsync(key, amount, expiration, cancellationToken);

        public Task<double> SetIfHigherAsync(string key, double value, TimeSpan? expiration, CancellationToken cancellationToken = default)
            => inner.SetIfHigherAsync(key, value, expiration, cancellationToken);

        public Task<long> SetIfHigherAsync(string key, long value, TimeSpan? expiration, CancellationToken cancellationToken = default)
            => inner.SetIfHigherAsync(key, value, expiration, cancellationToken);

        public Task<double> SetIfLowerAsync(string key, double value, TimeSpan? expiration, CancellationToken cancellationToken = default)
            => inner.SetIfLowerAsync(key, value, expiration, cancellationToken);

        public Task<long> SetIfLowerAsync(string key, long value, TimeSpan? expiration, CancellationToken cancellationToken = default)
            => inner.SetIfLowerAsync(key, value, expiration, cancellationToken);

        public Task<long> SetAddAsync<T>(string key, IEnumerable<T> value, TimeSpan? expiration, CancellationToken cancellationToken = default)
            => inner.SetAddAsync(key, value, expiration, cancellationToken);

        public Task<IDictionary<string, CacheValue<T>>> GetAllAsync<T>(IEnumerable<string> cacheKeys, CancellationToken cancellationToken = default)
            => inner.GetAllAsync<T>(cacheKeys, cancellationToken);

        public Task<IDictionary<string, CacheValue<T>>> GetByPrefixAsync<T>(string prefix, CancellationToken cancellationToken = default)
            => inner.GetByPrefixAsync<T>(prefix, cancellationToken);

        public Task<IReadOnlyList<string>> GetAllKeysByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
            => inner.GetAllKeysByPrefixAsync(prefix, cancellationToken);

        public Task<CacheValue<T>> GetAsync<T>(string key, CancellationToken cancellationToken = default)
            => inner.GetAsync<T>(key, cancellationToken);

        public Task<int> GetCountAsync(string prefix = "", CancellationToken cancellationToken = default)
            => inner.GetCountAsync(prefix, cancellationToken);

        public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
            => inner.ExistsAsync(key, cancellationToken);

        public Task<TimeSpan?> GetExpirationAsync(string key, CancellationToken cancellationToken = default)
            => inner.GetExpirationAsync(key, cancellationToken);

        public Task<CacheValue<ICollection<T>>> GetSetAsync<T>(string key, int? pageIndex = null, int pageSize = 100, CancellationToken cancellationToken = default)
            => inner.GetSetAsync<T>(key, pageIndex, pageSize, cancellationToken);

        public Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
            => inner.RemoveAsync(key, cancellationToken);

        public Task<bool> RemoveIfEqualAsync<T>(string key, T? expected, CancellationToken cancellationToken = default)
            => inner.RemoveIfEqualAsync(key, expected, cancellationToken);

        public Task<int> RemoveAllAsync(IEnumerable<string> cacheKeys, CancellationToken cancellationToken = default)
            => inner.RemoveAllAsync(cacheKeys, cancellationToken);

        public Task<int> RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
            => inner.RemoveByPrefixAsync(prefix, cancellationToken);

        public Task<long> SetRemoveAsync<T>(string key, IEnumerable<T> value, TimeSpan? expiration, CancellationToken cancellationToken = default)
            => inner.SetRemoveAsync(key, value, expiration, cancellationToken);

        public Task FlushAsync(CancellationToken cancellationToken = default)
            => inner.FlushAsync(cancellationToken);
    }

    private static ICache CreateTestCache(ICache mock) => new TestCache(mock);

    #region ICache Tests

    [Fact]
    public async Task should_return_cached_value_when_exists()
    {
        // given
        var mock = Substitute.For<ICache>();
        var cache = CreateTestCache(mock);
        var expectedValue = "cached-value";
        mock
            .GetAsync<string>("key", Arg.Any<CancellationToken>())
            .Returns(new CacheValue<string>(expectedValue, hasValue: true));

        // when
        var factoryCalled = false;
        var result = await cache.GetOrAddAsync(
            "key",
            _ =>
            {
                factoryCalled = true;
                return Task.FromResult<string?>("factory-value");
            },
            TimeSpan.FromMinutes(5),
            AbortToken
        );

        // then
        result.HasValue.Should().BeTrue();
        result.Value.Should().Be(expectedValue);
        factoryCalled.Should().BeFalse("factory should not be called on cache hit");
        await mock.Received(1).GetAsync<string>("key", Arg.Any<CancellationToken>());
        await mock
            .DidNotReceive()
            .UpsertAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_call_factory_when_cache_miss()
    {
        // given
        var mock = Substitute.For<ICache>();
        var cache = CreateTestCache(mock);
        mock.GetAsync<string>(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(CacheValue<string>.NoValue);
        mock
            .UpsertAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var factoryValue = "new-value";

        // when
        var result = await cache.GetOrAddAsync(
            "key-miss",
            _ => Task.FromResult<string?>(factoryValue),
            TimeSpan.FromMinutes(5),
            AbortToken
        );

        // then
        result.HasValue.Should().BeTrue();
        result.Value.Should().Be(factoryValue);
        await mock.Received(2).GetAsync<string>("key-miss", Arg.Any<CancellationToken>()); // first check + double-check
        await mock.Received(1).UpsertAsync("key-miss", factoryValue, TimeSpan.FromMinutes(5), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_execute_factory_only_once_for_concurrent_requests()
    {
        // given
        var mock = Substitute.For<ICache>();
        var cache = CreateTestCache(mock);
        var factoryExecutionCount = 0;
        var factoryStarted = new TaskCompletionSource();
        var factoryCanComplete = new TaskCompletionSource();
        var cachedValue = CacheValue<string>.NoValue;

        // Simulate cache behavior: first GetAsync returns NoValue, after UpsertAsync returns cached value
        mock.GetAsync<string>(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ => cachedValue);
        mock
            .UpsertAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                cachedValue = new CacheValue<string>(callInfo.ArgAt<string>(1), hasValue: true);
                return true;
            });

        async Task<string?> Factory(CancellationToken _)
        {
            Interlocked.Increment(ref factoryExecutionCount);
            factoryStarted.TrySetResult();
            await factoryCanComplete.Task;
            return "value";
        }

        // when - start multiple concurrent requests
        const int concurrentRequests = 5;
        var tasks = new List<Task<CacheValue<string>>>();

        for (var i = 0; i < concurrentRequests; i++)
        {
            tasks.Add(
                Task.Run(
                    () => cache.GetOrAddAsync("same-key-stampede", Factory, TimeSpan.FromMinutes(5), AbortToken),
                    AbortToken
                )
            );
        }

        // Wait for first factory to start
        await factoryStarted.Task;
        await Task.Delay(100, AbortToken); // Give time for other tasks to attempt to acquire lock

        // Let factory complete
        factoryCanComplete.SetResult();

        var results = await Task.WhenAll(tasks);

        // then - factory should only execute once
        factoryExecutionCount.Should().Be(1, "factory should only execute once due to stampede protection");
        results
            .Should()
            .AllSatisfy(r =>
            {
                r.HasValue.Should().BeTrue();
                r.Value.Should().Be("value");
            });
    }

    [Fact]
    public async Task should_block_different_cache_instances_with_same_key()
    {
        // given - global locking means same key blocks across all cache instances
        var mock1 = Substitute.For<ICache>();
        var mock2 = Substitute.For<ICache>();
        var cache1 = CreateTestCache(mock1);
        var cache2 = CreateTestCache(mock2);

        var executionOrder = new List<int>();
        var factory1Started = new TaskCompletionSource();
        var factory1CanComplete = new TaskCompletionSource();

        mock1.GetAsync<string>(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(CacheValue<string>.NoValue);
        mock2.GetAsync<string>(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(CacheValue<string>.NoValue);
        mock1
            .UpsertAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(true);
        mock2
            .UpsertAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(true);

        // when
        var task1 = Task.Run(
            async () =>
            {
                return await cache1.GetOrAddAsync(
                    "same-key-global",
                    async _ =>
                    {
                        executionOrder.Add(1);
                        factory1Started.SetResult();
                        await factory1CanComplete.Task;
                        return "value1";
                    },
                    TimeSpan.FromMinutes(5),
                    AbortToken
                );
            },
            AbortToken
        );

        // Wait for first factory to start
        await factory1Started.Task;

        var task2 = Task.Run(
            async () =>
            {
                return await cache2.GetOrAddAsync(
                    "same-key-global",
                    _ =>
                    {
                        executionOrder.Add(2);
                        return Task.FromResult<string?>("value2");
                    },
                    TimeSpan.FromMinutes(5),
                    AbortToken
                );
            },
            AbortToken
        );

        // Give task2 time to block on the lock
        await Task.Delay(100, AbortToken);

        // Release factory1
        factory1CanComplete.SetResult();

        await Task.WhenAll(task1, task2);

        // then - with global locking, task2's factory runs after task1 completes
        executionOrder.Should().ContainInOrder(1, 2);
    }

    [Fact]
    public async Task should_propagate_cancellation_token_to_factory()
    {
        // given
        var mock = Substitute.For<ICache>();
        var cache = CreateTestCache(mock);
        mock.GetAsync<string>(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(CacheValue<string>.NoValue);

        CancellationToken receivedToken = default;

        // when
        using var cts = new CancellationTokenSource();
        var expectedToken = cts.Token;

        await cache.GetOrAddAsync(
            "key-ct",
            ct =>
            {
                receivedToken = ct;
                return Task.FromResult<string?>("value");
            },
            TimeSpan.FromMinutes(5),
            expectedToken
        );

        // then
        receivedToken.Should().Be(expectedToken);
    }

    [Fact]
    public async Task should_release_lock_on_factory_exception()
    {
        // given
        var mock = Substitute.For<ICache>();
        var cache = CreateTestCache(mock);
        mock.GetAsync<string>(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(CacheValue<string>.NoValue);
        mock
            .UpsertAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(true);

        // when - first call throws exception
        var act = () =>
            cache.GetOrAddAsync<string>(
                "key-exception",
                _ => throw new InvalidOperationException("Factory failed"),
                TimeSpan.FromMinutes(5),
                AbortToken
            );

        await act.Should().ThrowAsync<InvalidOperationException>();

        // then - second call should succeed (lock was released)
        var result = await cache.GetOrAddAsync(
            "key-exception",
            _ => Task.FromResult<string?>("success"),
            TimeSpan.FromMinutes(5),
            AbortToken
        );

        result.HasValue.Should().BeTrue();
        result.Value.Should().Be("success");
    }

    [Fact]
    public async Task should_cache_null_value_from_factory()
    {
        // given
        var mock = Substitute.For<ICache>();
        var cache = CreateTestCache(mock);
        mock.GetAsync<string>(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(CacheValue<string>.NoValue);
        mock
            .UpsertAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(true);

        // when
        var result = await cache.GetOrAddAsync<string>(
            "key-null",
            _ => Task.FromResult<string?>(null),
            TimeSpan.FromMinutes(5),
            AbortToken
        );

        // then
        result.HasValue.Should().BeTrue();
        result.IsNull.Should().BeTrue();
        await mock
            .Received(1)
            .UpsertAsync("key-null", Arg.Is<string?>(v => v == null), TimeSpan.FromMinutes(5), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task should_throw_on_null_or_empty_key(string? key)
    {
        // given
        var mock = Substitute.For<ICache>();
        var cache = CreateTestCache(mock);

        // when
        var act = () =>
            cache.GetOrAddAsync(key!, _ => Task.FromResult<string?>("value"), TimeSpan.FromMinutes(5), AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task should_throw_on_null_factory()
    {
        // given
        var mock = Substitute.For<ICache>();
        var cache = CreateTestCache(mock);

        // when
        var act = () => cache.GetOrAddAsync<string>("key-null-factory", null!, TimeSpan.FromMinutes(5), AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task should_return_value_from_double_check_when_cached_during_lock_wait()
    {
        // given
        var mock = Substitute.For<ICache>();
        var cache = CreateTestCache(mock);
        var callCount = 0;

        // First call returns NoValue, subsequent calls return cached value
        mock
            .GetAsync<string>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callCount++;
                return callCount == 1
                    ? CacheValue<string>.NoValue
                    : new CacheValue<string>("cached-by-another", hasValue: true);
            });

        // when
        var result = await cache.GetOrAddAsync(
            "key-double-check",
            _ => Task.FromResult<string?>("factory-value"),
            TimeSpan.FromMinutes(5),
            AbortToken
        );

        // then - should return value from double-check, not factory
        result.HasValue.Should().BeTrue();
        result.Value.Should().Be("cached-by-another");
        await mock
            .DidNotReceive()
            .UpsertAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region ICache<T> Extension Tests

    [Fact]
    public async Task should_return_cached_value_for_typed_cache()
    {
        // given
        var cache = Substitute.For<ICache<string>>();
        var expectedValue = "typed-cached-value";
        cache
            .GetAsync("key", Arg.Any<CancellationToken>())
            .Returns(new CacheValue<string>(expectedValue, hasValue: true));

        // when
        var factoryCalled = false;
        var result = await cache.GetOrAddAsync(
            "key",
            _ =>
            {
                factoryCalled = true;
                return Task.FromResult<string?>("factory-value");
            },
            TimeSpan.FromMinutes(5),
            AbortToken
        );

        // then
        result.HasValue.Should().BeTrue();
        result.Value.Should().Be(expectedValue);
        factoryCalled.Should().BeFalse("factory should not be called on cache hit");
    }

    [Fact]
    public async Task should_provide_stampede_protection_for_typed_cache()
    {
        // given
        var cache = Substitute.For<ICache<string>>();
        var factoryExecutionCount = 0;
        var factoryStarted = new TaskCompletionSource();
        var factoryCanComplete = new TaskCompletionSource();
        var cachedValue = CacheValue<string>.NoValue;

        // Simulate cache behavior: first GetAsync returns NoValue, after UpsertAsync returns cached value
        cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ => cachedValue);
        cache
            .UpsertAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                cachedValue = new CacheValue<string>(callInfo.ArgAt<string>(1), hasValue: true);
                return true;
            });

        async Task<string?> Factory(CancellationToken _)
        {
            Interlocked.Increment(ref factoryExecutionCount);
            factoryStarted.TrySetResult();
            await factoryCanComplete.Task;
            return "typed-value";
        }

        // when
        const int concurrentRequests = 5;
        var tasks = new List<Task<CacheValue<string>>>();

        for (var i = 0; i < concurrentRequests; i++)
        {
            tasks.Add(
                Task.Run(
                    () => cache.GetOrAddAsync("typed-same-key", Factory, TimeSpan.FromMinutes(5), AbortToken),
                    AbortToken
                )
            );
        }

        await factoryStarted.Task;
        await Task.Delay(100, AbortToken);

        factoryCanComplete.SetResult();

        var results = await Task.WhenAll(tasks);

        // then
        factoryExecutionCount.Should().Be(1, "factory should only execute once for typed cache");
        results
            .Should()
            .AllSatisfy(r =>
            {
                r.HasValue.Should().BeTrue();
                r.Value.Should().Be("typed-value");
            });
    }

    [Fact]
    public async Task should_call_factory_and_cache_for_typed_cache_miss()
    {
        // given
        var cache = Substitute.For<ICache<string>>();
        cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(CacheValue<string>.NoValue);
        cache
            .UpsertAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(true);

        // when
        var result = await cache.GetOrAddAsync(
            "typed-key-miss",
            _ => Task.FromResult<string?>("factory-result"),
            TimeSpan.FromMinutes(5),
            AbortToken
        );

        // then
        result.HasValue.Should().BeTrue();
        result.Value.Should().Be("factory-result");
        await cache
            .Received(1)
            .UpsertAsync("typed-key-miss", "factory-result", TimeSpan.FromMinutes(5), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task should_throw_on_null_or_empty_key_for_typed_cache(string? key)
    {
        // given
        var cache = Substitute.For<ICache<string>>();

        // when
        var act = () =>
            cache.GetOrAddAsync(key!, _ => Task.FromResult<string?>("value"), TimeSpan.FromMinutes(5), AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task should_throw_on_null_factory_for_typed_cache()
    {
        // given
        var cache = Substitute.For<ICache<string>>();

        // when
        var act = () => cache.GetOrAddAsync("key", null!, TimeSpan.FromMinutes(5), AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task should_release_lock_on_factory_exception_for_typed_cache()
    {
        // given
        var cache = Substitute.For<ICache<string>>();
        cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(CacheValue<string>.NoValue);
        cache
            .UpsertAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(true);

        // when - first call throws
        var act = () =>
            cache.GetOrAddAsync(
                "typed-key-exception",
                _ => throw new InvalidOperationException("Typed factory failed"),
                TimeSpan.FromMinutes(5),
                AbortToken
            );

        await act.Should().ThrowAsync<InvalidOperationException>();

        // then - second call should succeed
        var result = await cache.GetOrAddAsync(
            "typed-key-exception",
            _ => Task.FromResult<string?>("success"),
            TimeSpan.FromMinutes(5),
            AbortToken
        );

        result.HasValue.Should().BeTrue();
        result.Value.Should().Be("success");
    }

    [Fact]
    public async Task should_block_different_typed_cache_instances_with_same_key()
    {
        // given - global locking means same key blocks across all cache instances
        var cache1 = Substitute.For<ICache<string>>();
        var cache2 = Substitute.For<ICache<string>>();

        var executionOrder = new List<int>();
        var factory1Started = new TaskCompletionSource();
        var factory1CanComplete = new TaskCompletionSource();

        cache1.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(CacheValue<string>.NoValue);
        cache2.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(CacheValue<string>.NoValue);
        cache1
            .UpsertAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(true);
        cache2
            .UpsertAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(true);

        // when
        var task1 = Task.Run(
            async () =>
            {
                return await cache1.GetOrAddAsync(
                    "typed-same-key-global",
                    async _ =>
                    {
                        executionOrder.Add(1);
                        factory1Started.SetResult();
                        await factory1CanComplete.Task;
                        return "value1";
                    },
                    TimeSpan.FromMinutes(5),
                    AbortToken
                );
            },
            AbortToken
        );

        // Wait for first factory to start
        await factory1Started.Task;

        var task2 = Task.Run(
            async () =>
            {
                return await cache2.GetOrAddAsync(
                    "typed-same-key-global",
                    _ =>
                    {
                        executionOrder.Add(2);
                        return Task.FromResult<string?>("value2");
                    },
                    TimeSpan.FromMinutes(5),
                    AbortToken
                );
            },
            AbortToken
        );

        // Give task2 time to block on the lock
        await Task.Delay(100, AbortToken);

        // Release factory1
        factory1CanComplete.SetResult();

        await Task.WhenAll(task1, task2);

        // then - with global locking, task2's factory runs after task1 completes
        executionOrder.Should().ContainInOrder(1, 2);
    }

    #endregion
}
