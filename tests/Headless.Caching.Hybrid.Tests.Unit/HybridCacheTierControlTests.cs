// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

/// <summary>
/// Per-call tier-write control (<see cref="CacheEntryOptions.SkipMemoryCacheWrite"/>,
/// <see cref="CacheEntryOptions.SkipDistributedCacheWrite"/>) and force-refresh
/// (<see cref="CacheEntryOptions.SkipCacheRead"/>). These flags are Hybrid-relevant; the tests exercise the L1+L2
/// composite store and assert the per-tier outcome directly.
/// </summary>
public sealed class HybridCacheTierControlTests : TestBase
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
        var l1 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions { CloneValues = true });
        var l2Inner = new InMemoryCache(_timeProvider, new InMemoryCacheOptions { CloneValues = true });
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

    private static CacheEntryOptions _Options(
        bool skipMemory = false,
        bool skipDistributed = false,
        bool skipRead = false,
        bool failSafe = false
    ) =>
        new()
        {
            Duration = TimeSpan.FromMinutes(5),
            SkipMemoryCacheWrite = skipMemory,
            SkipDistributedCacheWrite = skipDistributed,
            SkipCacheRead = skipRead,
            IsFailSafeEnabled = failSafe,
            FailSafeMaxDuration = TimeSpan.FromHours(1),
        };

    #region SkipCacheRead (force-refresh)

    [Fact]
    public async Task skip_cache_read_runs_factory_even_when_a_fresh_value_already_exists()
    {
        // given — a fresh value is already cached (both tiers populated by the first GetOrAdd).
        var (cache, _, _, _) = _CreateCache();
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        var factoryCalls = 0;

        var first = await cache.GetOrAddAsync(
            key,
            _ =>
            {
                Interlocked.Increment(ref factoryCalls);
                return new ValueTask<int?>(1);
            },
            _Options(),
            AbortToken
        );

        first.Value.Should().Be(1);
        factoryCalls.Should().Be(1);

        // when — force-refresh: SkipCacheRead must bypass the cached read and re-run the factory.
        var second = await cache.GetOrAddAsync(
            key,
            _ =>
            {
                Interlocked.Increment(ref factoryCalls);
                return new ValueTask<int?>(2);
            },
            _Options(skipRead: true),
            AbortToken
        );

        // then — the factory ran again and returned the fresh value.
        factoryCalls.Should().Be(2, "SkipCacheRead must force the factory to run even on a fresh hit");
        second.Value.Should().Be(2);
        second.IsStale.Should().BeFalse();
    }

    [Fact]
    public async Task skip_cache_read_with_failsafe_propagates_when_factory_throws_because_no_reserve_was_read()
    {
        // given — a physically-present stale reserve exists in both tiers that fail-safe would normally serve.
        var (cache, l1, l2, _) = _CreateCache();
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        var staleValue = Faker.Random.Int(1, 100);
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var staleEntry = new CacheStoreEntryWrite<int>
        {
            Value = staleValue,
            IsNull = false,
            LogicalExpiresAt = now.AddMinutes(-1), // logically stale
            PhysicalExpiresAt = now.AddHours(1), // physically held
        };
        await ((IFactoryCacheStore)l1).SetEntryAsync(key, staleEntry, AbortToken);
        await ((IFactoryCacheStore)l2).SetEntryAsync(key, staleEntry, AbortToken);

        // when / then — SkipCacheRead skips the reserve read, so a factory failure has nothing to fall back to and
        // propagates even with fail-safe enabled.
        var throwingFactory = new Func<CancellationToken, ValueTask<int?>>(_ =>
            throw new InvalidOperationException("upstream unavailable")
        );

        var act = async () =>
            await cache.GetOrAddAsync(key, throwingFactory, _Options(skipRead: true, failSafe: true), AbortToken);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    #endregion

    #region SkipMemoryCacheWrite

    [Fact]
    public async Task skip_memory_cache_write_via_factory_writes_only_l2()
    {
        // given
        var (cache, l1, l2, _) = _CreateCache();
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        var value = Faker.Random.Int(1, 100);

        // when — factory produces a fresh value with the L1 write suppressed.
        var result = await cache.GetOrAddAsync(
            key,
            _ => new ValueTask<int?>(value),
            _Options(skipMemory: true),
            AbortToken
        );

        // then — the caller saw the value, L1 is empty, L2 holds it.
        result.Value.Should().Be(value);
        (await l1.GetAsync<int>(key, AbortToken)).HasValue.Should().BeFalse("L1 write was skipped");
        (await l2.GetAsync<int>(key, AbortToken)).Value.Should().Be(value);
    }

    [Fact]
    public async Task skip_memory_cache_write_via_upsert_entry_writes_only_l2()
    {
        // given
        var (cache, l1, l2, _) = _CreateCache();
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        var value = Faker.Random.Int(1, 100);

        // when — direct options-bearing write with the L1 write suppressed.
        await cache.UpsertEntryAsync(key, value, _Options(skipMemory: true), AbortToken);

        // then
        (await l1.GetAsync<int>(key, AbortToken))
            .HasValue.Should()
            .BeFalse("L1 write was skipped");
        (await l2.GetAsync<int>(key, AbortToken)).Value.Should().Be(value);
    }

    #endregion

    #region SkipDistributedCacheWrite

    [Fact]
    public async Task skip_distributed_cache_write_via_factory_writes_only_l1()
    {
        // given
        var (cache, l1, l2, _) = _CreateCache();
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        var value = Faker.Random.Int(1, 100);

        // when
        var result = await cache.GetOrAddAsync(
            key,
            _ => new ValueTask<int?>(value),
            _Options(skipDistributed: true),
            AbortToken
        );

        // then — L1 holds it, L2 is empty.
        result.Value.Should().Be(value);
        (await l1.GetAsync<int>(key, AbortToken)).Value.Should().Be(value);
        (await l2.GetAsync<int>(key, AbortToken)).HasValue.Should().BeFalse("L2 write was skipped");
    }

    [Fact]
    public async Task skip_distributed_cache_write_via_upsert_entry_writes_only_l1()
    {
        // given
        var (cache, l1, l2, _) = _CreateCache();
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        var value = Faker.Random.Int(1, 100);

        // when
        await cache.UpsertEntryAsync(key, value, _Options(skipDistributed: true), AbortToken);

        // then
        (await l1.GetAsync<int>(key, AbortToken))
            .Value.Should()
            .Be(value);
        (await l2.GetAsync<int>(key, AbortToken)).HasValue.Should().BeFalse("L2 write was skipped");
    }

    #endregion

    #region All-false regression

    [Fact]
    public async Task all_flags_false_writes_both_tiers_as_before()
    {
        // given
        var (cache, l1, l2, _) = _CreateCache();
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        var value = Faker.Random.Int(1, 100);

        // when — default options: no tier control, no force-refresh.
        var result = await cache.GetOrAddAsync(key, _ => new ValueTask<int?>(value), _Options(), AbortToken);

        // then — both tiers populated, value returned.
        result.Value.Should().Be(value);
        (await l1.GetAsync<int>(key, AbortToken)).Value.Should().Be(value);
        (await l2.GetAsync<int>(key, AbortToken)).Value.Should().Be(value);
    }

    #endregion
}
