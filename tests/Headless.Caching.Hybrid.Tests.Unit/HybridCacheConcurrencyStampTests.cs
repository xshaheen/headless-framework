// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

/// <summary>
/// Compare-and-set behaviour of the hybrid factory store. The coordinator stamps a factory write with the
/// <see cref="CacheStoreEntry{T}.ConcurrencyStamp"/> of the snapshot it read, so a slow factory cannot resurrect a
/// key removed while it ran. The hybrid reads through one tier at a time, so it tags the stamp with its tier of
/// origin and applies the CAS to that tier — these tests pin both origins plus the control case where nothing races
/// the factory and the write must land.
/// </summary>
public sealed class HybridCacheConcurrencyStampTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();

    // The HybridCache returned here is disposed per test via `await using`, but it does not own the injected
    // L1/L2 stores. This fixture collects those raw InMemoryCache instances and disposes them at teardown.
    private readonly List<object> _disposables = [];

    private static readonly CacheEntryOptions _FailSafeOptions = new()
    {
        Duration = TimeSpan.FromMinutes(1),
        IsFailSafeEnabled = true,
        FailSafeMaxDuration = TimeSpan.FromMinutes(30),
        FailSafeThrottleDuration = TimeSpan.FromSeconds(5),
    };

    private (HybridCache cache, IInMemoryCache l1, IRemoteCache l2) _CreateCache()
    {
        var l1 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions { CloneValues = true });
        var l2Inner = new InMemoryCache(_timeProvider, new InMemoryCacheOptions { CloneValues = true });
        var l2 = new InMemoryRemoteCacheAdapter(l2Inner);

        var publisher = Substitute.For<IBus>();
        publisher
            .PublishAsync(Arg.Any<CacheInvalidationMessage>(), Arg.Any<PublishOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var cache = new HybridCache(l1, l2, publisher, new HybridCacheOptions(), timeProvider: _timeProvider);

        _disposables.Add(l1);
        _disposables.Add(l2Inner);

        return (cache, l1, l2);
    }

    /// <summary>
    /// Runs a GetOrAdd whose factory parks until released, so the caller can mutate the key mid-flight. Returns the
    /// in-flight call plus a releaser; the factory has provably started once this method returns.
    /// </summary>
    private static async Task<(Task<CacheValue<string>> Call, TaskCompletionSource Release)> _StartParkedFactoryAsync(
        HybridCache cache,
        string key,
        string value
    )
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var call = cache
            .GetOrAddAsync<string>(
                key,
                async _ =>
                {
                    started.TrySetResult();
                    await release.Task;

                    return value;
                },
                _FailSafeOptions,
                AbortToken
            )
            .AsTask();

        await started.Task.WaitAsync(TimeSpan.FromSeconds(30), AbortToken);

        return (call, release);
    }

    [Fact]
    public async Task should_not_resurrect_a_removed_key_when_the_factory_snapshot_came_from_l2()
    {
        // given — a stale (fail-safe) entry present on both tiers, so the coordinator's snapshot is the L2 one
        var (cache, l1, l2) = _CreateCache();
        await using var _ = cache;
        var key = Faker.Random.AlphaNumeric(10);

        await cache.GetOrAddAsync(key, _ => new ValueTask<string?>("v1"), _FailSafeOptions, AbortToken);
        _timeProvider.Advance(TimeSpan.FromMinutes(2));

        // when — the key is removed while the refresh factory is still running
        var (call, release) = await _StartParkedFactoryAsync(cache, key, "v2");
        await cache.RemoveAsync(key, AbortToken);
        release.SetResult();
        var result = await call;

        // then — the caller still gets the fresh value, but neither tier keeps it: the remove won the race
        result.Value.Should().Be("v2");
        (await l1.GetAsync<string>(key, AbortToken)).HasValue.Should().BeFalse("L1 must not be resurrected");
        (await l2.GetAsync<string>(key, AbortToken)).HasValue.Should().BeFalse("L2 must not be resurrected");
    }

    [Fact]
    public async Task should_not_resurrect_a_removed_key_when_the_factory_snapshot_came_from_l1()
    {
        // given — L2 dropped the key (shorter retention) while L1 still holds its fail-safe reserve, so the
        // coordinator's snapshot is the L1 one
        var (cache, l1, l2) = _CreateCache();
        await using var _ = cache;
        var key = Faker.Random.AlphaNumeric(10);

        await cache.GetOrAddAsync(key, _ => new ValueTask<string?>("v1"), _FailSafeOptions, AbortToken);
        await l2.RemoveAsync(key, AbortToken);
        _timeProvider.Advance(TimeSpan.FromMinutes(2));

        // when
        var (call, release) = await _StartParkedFactoryAsync(cache, key, "v2");
        await cache.RemoveAsync(key, AbortToken);
        release.SetResult();
        var result = await call;

        // then — the L1 CAS loses, and because the guarded tier is written first L2 is never touched either
        result.Value.Should().Be("v2");
        (await l1.GetAsync<string>(key, AbortToken)).HasValue.Should().BeFalse("L1 must not be resurrected");
        (await l2.GetAsync<string>(key, AbortToken)).HasValue.Should().BeFalse("L2 must not be resurrected");
    }

    [Fact]
    public async Task should_commit_the_factory_write_to_both_tiers_when_nothing_races_it()
    {
        // given — the same stale-refresh shape as above, with no concurrent remove: the CAS must match and commit
        var (cache, l1, l2) = _CreateCache();
        await using var _ = cache;
        var key = Faker.Random.AlphaNumeric(10);

        await cache.GetOrAddAsync(key, _ => new ValueTask<string?>("v1"), _FailSafeOptions, AbortToken);
        _timeProvider.Advance(TimeSpan.FromMinutes(2));

        // when
        var (call, release) = await _StartParkedFactoryAsync(cache, key, "v2");
        release.SetResult();
        var result = await call;

        // then
        result.Value.Should().Be("v2");
        (await l1.GetAsync<string>(key, AbortToken)).Value.Should().Be("v2");
        (await l2.GetAsync<string>(key, AbortToken)).Value.Should().Be("v2");
    }

    [Fact]
    public async Task should_commit_the_factory_write_when_the_snapshot_came_from_l1_and_nothing_races_it()
    {
        // given — L1-origin snapshot (L2 dropped the key), no concurrent remove
        var (cache, l1, l2) = _CreateCache();
        await using var _ = cache;
        var key = Faker.Random.AlphaNumeric(10);

        await cache.GetOrAddAsync(key, _ => new ValueTask<string?>("v1"), _FailSafeOptions, AbortToken);
        await l2.RemoveAsync(key, AbortToken);
        _timeProvider.Advance(TimeSpan.FromMinutes(2));

        // when
        var (call, release) = await _StartParkedFactoryAsync(cache, key, "v2");
        release.SetResult();
        var result = await call;

        // then — the L1 CAS matches the snapshot it read, so both tiers take the value
        result.Value.Should().Be("v2");
        (await l1.GetAsync<string>(key, AbortToken)).Value.Should().Be("v2");
        (await l2.GetAsync<string>(key, AbortToken)).Value.Should().Be("v2");
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
}
