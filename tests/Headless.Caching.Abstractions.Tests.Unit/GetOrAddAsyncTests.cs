// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

/// <summary>
/// Tests for <see cref="ICache.GetOrAddAsync{T}"/> with cache stampede protection.
/// Uses <see cref="InMemoryCache"/> as the test implementation.
/// </summary>
public sealed class GetOrAddAsyncTests : TestBase
{
    private static readonly TimeSpan _DefaultExpiration = TimeSpan.FromMinutes(5);
    private readonly FakeTimeProvider _timeProvider = new();

    private InMemoryCache _CreateCache() => new(_timeProvider, new InMemoryCacheOptions());

    [Fact]
    public async Task should_return_cached_value_when_exists()
    {
        // given
        using var cache = _CreateCache();
        await cache.UpsertAsync("key", "cached", _DefaultExpiration, AbortToken);

        // when
        var result = await cache.GetOrAddAsync<string>(
            "key",
            _ => ValueTask.FromResult<string?>("new-value"),
            _DefaultExpiration,
            AbortToken
        );

        // then
        result.HasValue.Should().BeTrue();
        result.Value.Should().Be("cached");
    }

    [Fact]
    public async Task should_call_factory_when_not_cached()
    {
        // given
        using var cache = _CreateCache();
        var factoryCalled = false;

        // when
        var result = await cache.GetOrAddAsync<string>(
            "key",
            _ =>
            {
                factoryCalled = true;
                return ValueTask.FromResult<string?>("factory-value");
            },
            _DefaultExpiration,
            AbortToken
        );

        // then
        factoryCalled.Should().BeTrue();
        result.HasValue.Should().BeTrue();
        result.Value.Should().Be("factory-value");

        // Verify value was cached
        var cached = await cache.GetAsync<string>("key", AbortToken);
        cached.Value.Should().Be("factory-value");
    }

    [Fact]
    public async Task should_call_factory_only_once_for_concurrent_requests()
    {
        // given
        using var cache = _CreateCache();
        var factoryCallCount = 0;
        var factoryStarted = new TaskCompletionSource();
        var factoryCanComplete = new TaskCompletionSource();

        ValueTask<string?> slowFactory(CancellationToken ct)
        {
            return new ValueTask<string?>(SlowFactoryAsync());

            async Task<string?> SlowFactoryAsync()
            {
                Interlocked.Increment(ref factoryCallCount);
                factoryStarted.TrySetResult();
                await factoryCanComplete.Task;
                return "value";
            }
        }

        // when - start two concurrent requests
        var task1 = cache.GetOrAddAsync<string>("same-key", slowFactory, _DefaultExpiration, AbortToken);
        await factoryStarted.Task;

        var task2Started = new TaskCompletionSource();
        var task2 = Task.Run(
            async () =>
            {
                task2Started.SetResult();
                return await cache.GetOrAddAsync<string>("same-key", slowFactory, _DefaultExpiration, AbortToken);
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
    public async Task should_allow_concurrent_requests_for_different_keys()
    {
        // given
        using var cache = _CreateCache();
        var key1Started = new TaskCompletionSource();
        var key2Started = new TaskCompletionSource();
        var bothAcquired = new TaskCompletionSource();

        // when
        var task1 = Task.Run(
            async () =>
            {
                return await cache.GetOrAddAsync<string>(
                    "key1",
                    async _ =>
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
                return await cache.GetOrAddAsync<string>(
                    "key2",
                    async _ =>
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
        using var cache = _CreateCache();

        // when
        var result = await cache.GetOrAddAsync<string>(
            "key",
            _ => ValueTask.FromResult<string?>(null),
            _DefaultExpiration,
            AbortToken
        );

        // then
        result.HasValue.Should().BeTrue();
        result.Value.Should().BeNull();

        // Verify null was cached
        var cached = await cache.GetAsync<string>("key", AbortToken);
        cached.HasValue.Should().BeTrue();
        cached.Value.Should().BeNull();
    }

    [Fact]
    public async Task should_use_instance_based_locking_not_global()
    {
        // given - two different cache instances with same key should NOT share locks
        using var cache1 = _CreateCache();
        using var cache2 = _CreateCache();

        var factory1Started = new TaskCompletionSource();
        var factory1CanComplete = new TaskCompletionSource();
        var factory2Started = new TaskCompletionSource();

        // when
        var task1 = Task.Run(
            async () =>
            {
                return await cache1.GetOrAddAsync<string>(
                    "shared-key",
                    async _ =>
                    {
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

        var task2 = Task.Run(
            async () =>
            {
                return await cache2.GetOrAddAsync<string>(
                    "shared-key",
                    _ =>
                    {
                        factory2Started.SetResult();
                        return ValueTask.FromResult<string?>("value2");
                    },
                    _DefaultExpiration,
                    AbortToken
                );
            },
            AbortToken
        );

        // then - factory2 should start immediately (instance-based locking, not global)
        var factory2StartedResult = await Task.WhenAny(factory2Started.Task, Task.Delay(500, AbortToken));
        factory2StartedResult
            .Should()
            .Be(factory2Started.Task, "factory2 should start immediately because cache2 has its own lock");

        factory1CanComplete.SetResult();
        await Task.WhenAll(task1, task2);
    }

    [Fact]
    public async Task should_propagate_factory_exception()
    {
        // given
        using var cache = _CreateCache();

        // when
        var act = () =>
            cache
                .GetOrAddAsync<string>(
                    "key",
                    _ => throw new InvalidOperationException("Factory failed"),
                    _DefaultExpiration,
                    AbortToken
                )
                .AsTask();

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Factory failed");
    }

    [Fact]
    public async Task should_release_lock_on_factory_exception()
    {
        // given
        using var cache = _CreateCache();

        // when - first call throws
        try
        {
            await cache.GetOrAddAsync<string>(
                "exception-key",
                _ => throw new InvalidOperationException("Factory failed"),
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
                await cache.GetOrAddAsync<string>(
                    "exception-key",
                    _ => ValueTask.FromResult<string?>("recovered"),
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

    [Fact]
    public async Task should_pass_cancellation_token_to_factory()
    {
        // given
        using var cache = _CreateCache();
        using var cts = new CancellationTokenSource();
        CancellationToken receivedToken = default;

        // when
        await cache.GetOrAddAsync<string>(
            "key",
            ct =>
            {
                receivedToken = ct;
                return ValueTask.FromResult<string?>("value");
            },
            _DefaultExpiration,
            cts.Token
        );

        // then
        receivedToken.Should().Be(cts.Token);
    }

    [Fact]
    public async Task should_support_synchronous_factory_efficiently()
    {
        // given - ValueTask allows efficient sync completion
        using var cache = _CreateCache();

        // when - factory returns synchronously
        var result = await cache.GetOrAddAsync(
            "sync-key",
            _ => ValueTask.FromResult<int?>(42), // No allocation for sync completion
            _DefaultExpiration,
            AbortToken
        );

        // then
        result.Value.Should().Be(42);
    }
}
