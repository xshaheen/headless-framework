// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Caching;
using Xunit;
using Xunit.v3;

namespace Framework.Caching.Tests.Unit;

#pragma warning disable VSTHRD003 // Avoid awaiting foreign Tasks
public sealed class CacheExtensionsTests
{
    private static CancellationToken AbortToken => TestContext.Current.CancellationToken;

    #region ICache Extension Tests

    [Fact]
    public async Task should_return_cached_value_when_exists()
    {
        // given
        var cache = Substitute.For<ICache>();
        var expectedValue = "cached-value";
        cache
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
        await cache.Received(1).GetAsync<string>("key", Arg.Any<CancellationToken>());
        await cache
            .DidNotReceive()
            .UpsertAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_call_factory_when_cache_miss()
    {
        // given
        var cache = Substitute.For<ICache>();
        cache.GetAsync<string>(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(CacheValue<string>.NoValue);
        cache
            .UpsertAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var factoryValue = "new-value";

        // when
        var result = await cache.GetOrAddAsync(
            "key",
            _ => Task.FromResult<string?>(factoryValue),
            TimeSpan.FromMinutes(5),
            AbortToken
        );

        // then
        result.HasValue.Should().BeTrue();
        result.Value.Should().Be(factoryValue);
        await cache.Received(2).GetAsync<string>("key", Arg.Any<CancellationToken>()); // first check + double-check
        await cache.Received(1).UpsertAsync("key", factoryValue, TimeSpan.FromMinutes(5), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_execute_factory_only_once_for_concurrent_requests()
    {
        // given
        var cache = Substitute.For<ICache>();
        var factoryExecutionCount = 0;
        var factoryStarted = new TaskCompletionSource();
        var factoryCanComplete = new TaskCompletionSource();
        var cachedValue = CacheValue<string>.NoValue;

        // Simulate cache behavior: first GetAsync returns NoValue, after UpsertAsync returns cached value
        cache.GetAsync<string>(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ => cachedValue);
        cache
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
                    () => cache.GetOrAddAsync("same-key", Factory, TimeSpan.FromMinutes(5), AbortToken),
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
    public async Task should_allow_different_cache_instances_to_run_factories_concurrently()
    {
        // given
        var cache1 = Substitute.For<ICache>();
        var cache2 = Substitute.For<ICache>();

        var factory1Started = new TaskCompletionSource();
        var factory2Started = new TaskCompletionSource();
        var bothStarted = new TaskCompletionSource();

        cache1.GetAsync<string>(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(CacheValue<string>.NoValue);
        cache2.GetAsync<string>(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(CacheValue<string>.NoValue);
        cache1
            .UpsertAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(true);
        cache2
            .UpsertAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(true);

        // when
        var task1 = Task.Run(
            async () =>
            {
                return await cache1.GetOrAddAsync(
                    "same-key",
                    async _ =>
                    {
                        factory1Started.SetResult();
                        await bothStarted.Task;
                        return "value1";
                    },
                    TimeSpan.FromMinutes(5),
                    AbortToken
                );
            },
            AbortToken
        );

        var task2 = Task.Run(
            async () =>
            {
                return await cache2.GetOrAddAsync(
                    "same-key",
                    async _ =>
                    {
                        factory2Started.SetResult();
                        await bothStarted.Task;
                        return "value2";
                    },
                    TimeSpan.FromMinutes(5),
                    AbortToken
                );
            },
            AbortToken
        );

        // then - both factories should start (different cache instances have separate locks)
        var bothFactoriesStarted = Task.WhenAll(factory1Started.Task, factory2Started.Task);
        var completed = await Task.WhenAny(bothFactoriesStarted, Task.Delay(1000, AbortToken));

        completed
            .Should()
            .Be(bothFactoriesStarted, "both factories should run concurrently for different cache instances");

        bothStarted.SetResult();
        await Task.WhenAll(task1, task2);
    }

    [Fact]
    public async Task should_propagate_cancellation_token_to_factory()
    {
        // given
        var cache = Substitute.For<ICache>();
        cache.GetAsync<string>(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(CacheValue<string>.NoValue);

        CancellationToken receivedToken = default;

        // when
        using var cts = new CancellationTokenSource();
        var expectedToken = cts.Token;

        await cache.GetOrAddAsync(
            "key",
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
        var cache = Substitute.For<ICache>();
        cache.GetAsync<string>(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(CacheValue<string>.NoValue);
        cache
            .UpsertAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(true);

        // when - first call throws exception
        var act = () =>
            cache.GetOrAddAsync<string>(
                "key",
                _ => throw new InvalidOperationException("Factory failed"),
                TimeSpan.FromMinutes(5),
                AbortToken
            );

        await act.Should().ThrowAsync<InvalidOperationException>();

        // then - second call should succeed (lock was released)
        var result = await cache.GetOrAddAsync(
            "key",
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
        var cache = Substitute.For<ICache>();
        cache.GetAsync<string>(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(CacheValue<string>.NoValue);
        cache
            .UpsertAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(true);

        // when
        var result = await cache.GetOrAddAsync<string>(
            "key",
            _ => Task.FromResult<string?>(null),
            TimeSpan.FromMinutes(5),
            AbortToken
        );

        // then
        result.HasValue.Should().BeTrue();
        result.IsNull.Should().BeTrue();
        await cache
            .Received(1)
            .UpsertAsync("key", Arg.Is<string?>(v => v == null), TimeSpan.FromMinutes(5), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task should_throw_on_null_or_empty_key(string? key)
    {
        // given
        var cache = Substitute.For<ICache>();

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
        var cache = Substitute.For<ICache>();

        // when
        var act = () => cache.GetOrAddAsync<string>("key", null!, TimeSpan.FromMinutes(5), AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task should_return_value_from_double_check_when_cached_during_lock_wait()
    {
        // given
        var cache = Substitute.For<ICache>();
        var callCount = 0;

        // First call returns NoValue, subsequent calls return cached value
        cache
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
            "key",
            _ => Task.FromResult<string?>("factory-value"),
            TimeSpan.FromMinutes(5),
            AbortToken
        );

        // then - should return value from double-check, not factory
        result.HasValue.Should().BeTrue();
        result.Value.Should().Be("cached-by-another");
        await cache
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
                    () => cache.GetOrAddAsync("same-key", Factory, TimeSpan.FromMinutes(5), AbortToken),
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
            "key",
            _ => Task.FromResult<string?>("factory-result"),
            TimeSpan.FromMinutes(5),
            AbortToken
        );

        // then
        result.HasValue.Should().BeTrue();
        result.Value.Should().Be("factory-result");
        await cache
            .Received(1)
            .UpsertAsync("key", "factory-result", TimeSpan.FromMinutes(5), Arg.Any<CancellationToken>());
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
                "key",
                _ => throw new InvalidOperationException("Typed factory failed"),
                TimeSpan.FromMinutes(5),
                AbortToken
            );

        await act.Should().ThrowAsync<InvalidOperationException>();

        // then - second call should succeed
        var result = await cache.GetOrAddAsync(
            "key",
            _ => Task.FromResult<string?>("success"),
            TimeSpan.FromMinutes(5),
            AbortToken
        );

        result.HasValue.Should().BeTrue();
        result.Value.Should().Be("success");
    }

    [Fact]
    public async Task should_allow_different_typed_cache_instances_to_run_concurrently()
    {
        // given
        var cache1 = Substitute.For<ICache<string>>();
        var cache2 = Substitute.For<ICache<string>>();

        var factory1Started = new TaskCompletionSource();
        var factory2Started = new TaskCompletionSource();
        var bothStarted = new TaskCompletionSource();

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
                    "same-key",
                    async _ =>
                    {
                        factory1Started.SetResult();
                        await bothStarted.Task;
                        return "value1";
                    },
                    TimeSpan.FromMinutes(5),
                    AbortToken
                );
            },
            AbortToken
        );

        var task2 = Task.Run(
            async () =>
            {
                return await cache2.GetOrAddAsync(
                    "same-key",
                    async _ =>
                    {
                        factory2Started.SetResult();
                        await bothStarted.Task;
                        return "value2";
                    },
                    TimeSpan.FromMinutes(5),
                    AbortToken
                );
            },
            AbortToken
        );

        // then
        var bothFactoriesStarted = Task.WhenAll(factory1Started.Task, factory2Started.Task);
        var completed = await Task.WhenAny(bothFactoriesStarted, Task.Delay(1000, AbortToken));

        completed.Should().Be(bothFactoriesStarted, "both typed cache factories should run concurrently");

        bothStarted.SetResult();
        await Task.WhenAll(task1, task2);
    }

    #endregion
}
