// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Testing.Tests;
using NSubstitute;

namespace Tests;

/// <summary>
/// Tests for the <see cref="CacheExtensions.GetOrAddAsync{T}"/> extension method with cache stampede protection.
/// </summary>
public sealed class GetOrAddAsyncTests : TestBase
{
    private static readonly TimeSpan _DefaultExpiration = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task should_return_cached_value_when_exists()
    {
        // given
        var mock = Substitute.For<ICache>();
        mock.GetAsync<string>("key", Arg.Any<CancellationToken>())
            .Returns(new CacheValue<string>("cached", true));

        // when
        var result = await mock.GetOrAddAsync<string>(
            "key",
            () => Task.FromResult<string?>("new-value"),
            _DefaultExpiration,
            AbortToken
        );

        // then
        result.HasValue.Should().BeTrue();
        result.Value.Should().Be("cached");
        await mock.DidNotReceive()
            .UpsertAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_call_factory_when_not_cached()
    {
        // given
        var mock = Substitute.For<ICache>();
        mock.GetAsync<string>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(CacheValue<string>.NoValue);
        var factoryCalled = false;

        // when
        var result = await mock.GetOrAddAsync<string>(
            "key",
            () =>
            {
                factoryCalled = true;
                return Task.FromResult<string?>("factory-value");
            },
            _DefaultExpiration,
            AbortToken
        );

        // then
        factoryCalled.Should().BeTrue();
        result.HasValue.Should().BeTrue();
        result.Value.Should().Be("factory-value");
        await mock.Received(1)
            .UpsertAsync("key", "factory-value", _DefaultExpiration, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_call_factory_only_once_for_concurrent_requests()
    {
        // given
        var mock = Substitute.For<ICache>();
        var cachedValue = (string?)null;

        // Simulate real cache behavior: return NoValue until value is cached
        mock.GetAsync<string>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => cachedValue is null
                ? CacheValue<string>.NoValue
                : new CacheValue<string>(cachedValue, true));

        // When upsert is called, store the value
        mock.UpsertAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                cachedValue = call.ArgAt<string?>(1);
                return new ValueTask<bool>(true);
            });

        var factoryCallCount = 0;
        var factoryStarted = new TaskCompletionSource();
        var factoryCanComplete = new TaskCompletionSource();

        async Task<string?> slowFactory()
        {
            Interlocked.Increment(ref factoryCallCount);
            factoryStarted.TrySetResult();
            await factoryCanComplete.Task;
            return "value";
        }

        // when - start two concurrent requests
        var task1 = mock.GetOrAddAsync<string>("same-key", slowFactory, _DefaultExpiration, AbortToken);
        await factoryStarted.Task;

        var task2Started = new TaskCompletionSource();
        var task2 = Task.Run(
            async () =>
            {
                task2Started.SetResult();
                return await mock.GetOrAddAsync<string>("same-key", slowFactory, _DefaultExpiration, AbortToken);
            },
            AbortToken
        );
        await task2Started.Task;
        await Task.Delay(50, AbortToken); // Give task2 time to block

        factoryCanComplete.SetResult();
        var results = await Task.WhenAll(task1.AsTask(), task2);

        // then - factory called exactly once due to keyed locking + double-check
        factoryCallCount.Should().Be(1);
        results.Should().AllSatisfy(r => r.Value.Should().Be("value"));
    }

    [Fact]
    public async Task should_return_cached_value_on_double_check()
    {
        // given - simulate value being cached between first check and lock acquisition
        var mock = Substitute.For<ICache>();
        var getCallCount = 0;
        mock.GetAsync<string>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                getCallCount++;
                // First call returns no value, second call (after lock) returns cached value
                return getCallCount == 1
                    ? CacheValue<string>.NoValue
                    : new CacheValue<string>("cached-by-another", true);
            });

        // when
        var result = await mock.GetOrAddAsync<string>(
            "key",
            () => Task.FromResult<string?>("factory-value"),
            _DefaultExpiration,
            AbortToken
        );

        // then - should return value from double-check, not factory
        result.Value.Should().Be("cached-by-another");
        await mock.DidNotReceive()
            .UpsertAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_allow_concurrent_requests_for_different_keys()
    {
        // given
        var mock = Substitute.For<ICache>();
        mock.GetAsync<string>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(CacheValue<string>.NoValue);

        var key1Started = new TaskCompletionSource();
        var key2Started = new TaskCompletionSource();
        var bothAcquired = new TaskCompletionSource();

        // when
        var task1 = Task.Run(
            async () =>
            {
                return await mock.GetOrAddAsync<string>(
                    "key1",
                    async () =>
                    {
                        key1Started.SetResult();
                        await bothAcquired.Task;
                        return "value1";
                    },
                    _DefaultExpiration,
                    AbortToken
                );
            },
            AbortToken
        );

        var task2 = Task.Run(
            async () =>
            {
                return await mock.GetOrAddAsync<string>(
                    "key2",
                    async () =>
                    {
                        key2Started.SetResult();
                        await bothAcquired.Task;
                        return "value2";
                    },
                    _DefaultExpiration,
                    AbortToken
                );
            },
            AbortToken
        );

        // then - both factories should start concurrently (different keys don't block each other)
        var bothStarted = Task.WhenAll(key1Started.Task, key2Started.Task);
        var completed = await Task.WhenAny(bothStarted, Task.Delay(1000, AbortToken));
        completed.Should().Be(bothStarted, "different keys should not block each other");

        bothAcquired.SetResult();
        var results = await Task.WhenAll(task1, task2);
        results.Select(r => r.Value).Should().Contain("value1").And.Contain("value2");
    }

    [Fact]
    public async Task should_handle_factory_returning_null()
    {
        // given
        var mock = Substitute.For<ICache>();
        mock.GetAsync<string>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(CacheValue<string>.NoValue);

        // when
        var result = await mock.GetOrAddAsync<string>(
            "key",
            () => Task.FromResult<string?>(null),
            _DefaultExpiration,
            AbortToken
        );

        // then
        result.HasValue.Should().BeTrue();
        result.Value.Should().BeNull();
        await mock.Received(1)
            .UpsertAsync("key", default(string), _DefaultExpiration, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_use_global_locking_across_cache_instances()
    {
        // given - two different cache instances but same key should block
        var mock1 = Substitute.For<ICache>();
        var mock2 = Substitute.For<ICache>();
        mock1.GetAsync<string>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(CacheValue<string>.NoValue);
        mock2.GetAsync<string>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(CacheValue<string>.NoValue);

        var factoryCallCount = 0;
        var factory1Started = new TaskCompletionSource();
        var factory1CanComplete = new TaskCompletionSource();
        var factory2Started = new TaskCompletionSource();

        // when
        var task1 = Task.Run(
            async () =>
            {
                return await mock1.GetOrAddAsync<string>(
                    "global-lock-key",
                    async () =>
                    {
                        Interlocked.Increment(ref factoryCallCount);
                        factory1Started.SetResult();
                        await factory1CanComplete.Task;
                        return "value1";
                    },
                    _DefaultExpiration,
                    AbortToken
                );
            },
            AbortToken
        );

        await factory1Started.Task;

        var task2Started = new TaskCompletionSource();
        var task2 = Task.Run(
            async () =>
            {
                task2Started.SetResult();
                return await mock2.GetOrAddAsync<string>(
                    "global-lock-key",
                    () =>
                    {
                        Interlocked.Increment(ref factoryCallCount);
                        factory2Started.SetResult();
                        return Task.FromResult<string?>("value2");
                    },
                    _DefaultExpiration,
                    AbortToken
                );
            },
            AbortToken
        );

        await task2Started.Task;
        await Task.Delay(100, AbortToken); // Give task2 time to block on the lock

        // Assert: factory2 should NOT have started yet because factory1 holds the global lock
        factory2Started.Task.IsCompleted.Should().BeFalse(
            "factory2 should be blocked waiting for the global lock held by factory1"
        );

        // Release factory1 - this should allow factory2 to proceed
        factory1CanComplete.SetResult();
        await Task.WhenAll(task1, task2);

        // then - both factories called because different cache instances (no shared state for double-check)
        // but they execute sequentially due to global locking
        factoryCallCount.Should().Be(2);
    }

    [Fact]
    public async Task should_propagate_factory_exception()
    {
        // given
        var mock = Substitute.For<ICache>();
        mock.GetAsync<string>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(CacheValue<string>.NoValue);

        // when
        var act = () => mock.GetOrAddAsync<string>(
            "key",
            () => throw new InvalidOperationException("Factory failed"),
            _DefaultExpiration,
            AbortToken
        ).AsTask();

        // then
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Factory failed");
    }

    [Fact]
    public async Task should_release_lock_on_factory_exception()
    {
        // given
        var mock = Substitute.For<ICache>();
        mock.GetAsync<string>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(CacheValue<string>.NoValue);

        // when - first call throws
        try
        {
            await mock.GetOrAddAsync<string>(
                "exception-key",
                () => throw new InvalidOperationException("Factory failed"),
                _DefaultExpiration,
                AbortToken
            );
        }
        catch (InvalidOperationException)
        {
            // Expected
        }

        // then - second call should not deadlock (lock was released)
        var secondCallCompleted = false;
        var task = Task.Run(
            async () =>
            {
                await mock.GetOrAddAsync<string>(
                    "exception-key",
                    () => Task.FromResult<string?>("recovered"),
                    _DefaultExpiration,
                    AbortToken
                );
                secondCallCompleted = true;
            },
            AbortToken
        );

        var completed = await Task.WhenAny(task, Task.Delay(1000, AbortToken));
        completed.Should().Be(task, "second call should complete without deadlock");
        secondCallCompleted.Should().BeTrue();
    }
}
