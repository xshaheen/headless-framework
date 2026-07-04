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
        bool skipMemoryRead = false,
        bool skipDistributedRead = false,
        bool failSafe = false
    ) =>
        new()
        {
            Duration = TimeSpan.FromMinutes(5),
            SkipMemoryCacheWrite = skipMemory,
            SkipDistributedCacheWrite = skipDistributed,
            SkipCacheRead = skipRead,
            SkipMemoryCacheRead = skipMemoryRead,
            SkipDistributedCacheRead = skipDistributedRead,
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

    #region SkipMemoryCacheRead / SkipDistributedCacheRead (per-tier read skip)

    // Seeds L1 and L2 with distinct values using the per-tier WRITE-skip flags, so a read-skip test can prove which
    // tier actually served the value: an L2 read yields l2Value, an L1 read yields l1Value.
    private async Task _SeedDistinctTiersAsync(HybridCache cache, string key, int l1Value, int l2Value)
    {
        await cache.UpsertEntryAsync(key, l2Value, _Options(skipMemory: true), AbortToken); // L2 only
        await cache.UpsertEntryAsync(key, l1Value, _Options(skipDistributed: true), AbortToken); // L1 only
    }

    [Fact]
    public async Task skip_memory_cache_read_bypasses_l1_and_serves_from_l2()
    {
        // given — L1 holds 11, L2 holds 22 (distinct), and a factory that would return 33 if run.
        var (cache, l1, l2, _) = _CreateCache();
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        await _SeedDistinctTiersAsync(cache, key, l1Value: 11, l2Value: 22);
        var factoryCalls = 0;

        // when — SkipMemoryCacheRead must bypass L1's fresh 11 and read L2 instead.
        var result = await cache.GetOrAddAsync(
            key,
            _ =>
            {
                Interlocked.Increment(ref factoryCalls);
                return new ValueTask<int?>(33);
            },
            _Options(skipMemoryRead: true),
            AbortToken
        );

        // then — the value came from L2 (22), not L1 (11), and the factory never ran (L2 was a fresh hit).
        result.Value.Should().Be(22, "L1 read was skipped so the fresh L2 value is served");
        result.IsStale.Should().BeFalse();
        factoryCalls.Should().Be(0, "the L2 hit satisfied the read");
        // The fresh L2 entry is promoted into L1 (promotion is a write, not gated by the read skip).
        (await l1.GetAsync<int>(key, AbortToken))
            .Value.Should()
            .Be(22);
        (await l2.GetAsync<int>(key, AbortToken)).Value.Should().Be(22);
    }

    [Fact]
    public async Task skip_distributed_cache_read_serves_from_l1_without_reading_l2()
    {
        // given — L1 holds 11, L2 holds 22 (distinct), and a factory that would return 33 if run.
        var (cache, _, _, _) = _CreateCache();
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        await _SeedDistinctTiersAsync(cache, key, l1Value: 11, l2Value: 22);
        var factoryCalls = 0;

        // when — SkipDistributedCacheRead must serve the fresh L1 value and never consult L2.
        var result = await cache.GetOrAddAsync(
            key,
            _ =>
            {
                Interlocked.Increment(ref factoryCalls);
                return new ValueTask<int?>(33);
            },
            _Options(skipDistributedRead: true),
            AbortToken
        );

        // then — the value came from L1 (11), not L2 (22), and the factory never ran.
        result.Value.Should().Be(11, "the fresh L1 value is served without an L2 read");
        result.IsStale.Should().BeFalse();
        factoryCalls.Should().Be(0);
    }

    [Fact]
    public async Task skip_distributed_cache_read_with_l1_miss_runs_factory_without_reading_l2()
    {
        // given — only L2 holds a value (22); L1 is empty. A factory would return 33.
        var (cache, _, _, _) = _CreateCache();
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        await cache.UpsertEntryAsync(key, 22, _Options(skipMemory: true), AbortToken); // L2 only
        var factoryCalls = 0;

        // when — L1 misses and L2 is skipped, so the read is a miss and the factory runs.
        var result = await cache.GetOrAddAsync(
            key,
            _ =>
            {
                Interlocked.Increment(ref factoryCalls);
                return new ValueTask<int?>(33);
            },
            _Options(skipDistributedRead: true),
            AbortToken
        );

        // then — L2's 22 was never read; the factory produced 33.
        result.Value.Should().Be(33, "L2 was skipped so the L1 miss falls through to the factory");
        factoryCalls.Should().Be(1);
    }

    [Fact]
    public async Task both_read_flags_set_runs_factory_like_skip_cache_read()
    {
        // given — both tiers hold a fresh value (L1=11, L2=22), and a factory that returns 33.
        var (cache, _, _, _) = _CreateCache();
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        await _SeedDistinctTiersAsync(cache, key, l1Value: 11, l2Value: 22);
        var factoryCalls = 0;

        // when — both read-skips set reads neither tier: a miss, exactly like SkipCacheRead.
        var result = await cache.GetOrAddAsync(
            key,
            _ =>
            {
                Interlocked.Increment(ref factoryCalls);
                return new ValueTask<int?>(33);
            },
            _Options(skipMemoryRead: true, skipDistributedRead: true),
            AbortToken
        );

        // then — neither cached value was served; the factory ran (parity with SkipCacheRead).
        result.Value.Should().Be(33, "reading neither tier is a miss, so the factory runs");
        factoryCalls.Should().Be(1);
    }

    [Fact]
    public async Task neither_read_flag_set_reads_both_tiers_as_before()
    {
        // given — only L2 holds 22; L1 is empty. Regression guard for the default read path.
        var (cache, l1, _, _) = _CreateCache();
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        await cache.UpsertEntryAsync(key, 22, _Options(skipMemory: true), AbortToken); // L2 only
        var factoryCalls = 0;

        // when — no read-skip flags: L1 misses, L2 is read and hits.
        var result = await cache.GetOrAddAsync(
            key,
            _ =>
            {
                Interlocked.Increment(ref factoryCalls);
                return new ValueTask<int?>(33);
            },
            _Options(),
            AbortToken
        );

        // then — the fresh L2 value is served (factory not run) and promoted into L1, unchanged from before.
        result.Value.Should().Be(22);
        factoryCalls.Should().Be(0);
        (await l1.GetAsync<int>(key, AbortToken)).Value.Should().Be(22, "a fresh L2 hit is promoted into L1");
    }

    #endregion
}
