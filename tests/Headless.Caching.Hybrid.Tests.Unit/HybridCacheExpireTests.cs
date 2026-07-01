// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

/// <summary>
/// Behavioral tests for <see cref="HybridCache.ExpireAsync"/> — expires L1+L2, publishes an Expire
/// <see cref="CacheInvalidationMessage"/>, and (on the receive path) logically expires the peer L1 while
/// preserving its fail-safe reserve.
/// </summary>
public sealed class HybridCacheExpireTests : TestBase
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
        var l1Options = new InMemoryCacheOptions { CloneValues = true };
        var l1 = new InMemoryCache(_timeProvider, l1Options);

        var l2Options = new InMemoryCacheOptions { CloneValues = true };
        var l2Base = new InMemoryCache(_timeProvider, l2Options);
        var l2 = new InMemoryRemoteCacheAdapter(l2Base);

        var publisher = Substitute.For<IBus>();
        publisher
            .PublishAsync(Arg.Any<CacheInvalidationMessage>(), Arg.Any<PublishOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var cache = new HybridCache(l1, l2, publisher, options, timeProvider: _timeProvider);

        _disposables.Add(l1);
        _disposables.Add(l2Base);

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

    private static CacheEntryOptions _FailSafeOptions() =>
        new()
        {
            Duration = TimeSpan.FromMinutes(1),
            IsFailSafeEnabled = true,
            FailSafeMaxDuration = TimeSpan.FromMinutes(10),
            FailSafeThrottleDuration = TimeSpan.FromSeconds(30),
        };

    [Fact]
    public async Task should_expire_both_tiers_and_publish_expire_invalidation()
    {
        // given
        var (cache, l1, l2, publisher) = _CreateCache();
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        var value = Faker.Random.Int(1, 100);
        await cache.GetOrAddAsync(key, _ => ValueTask.FromResult<int?>(value), TimeSpan.FromMinutes(5), AbortToken);

        // when
        var expired = await cache.ExpireAsync(key, AbortToken);

        // then — both tiers expired (plain reads miss) and an Expire invalidation is published for this key
        expired.Should().BeTrue();
        (await l1.GetAsync<int>(key, AbortToken)).HasValue.Should().BeFalse();
        (await l2.GetAsync<int>(key, AbortToken)).HasValue.Should().BeFalse();

        await publisher
            .Received(1)
            .PublishAsync(
                Arg.Is<CacheInvalidationMessage>(m => m.Key == key && m.Expire),
                Arg.Any<PublishOptions?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_logically_expire_peer_l1_preserving_reserve_when_receiving_expire_invalidation()
    {
        // given — a fail-safe entry (Physical > Logical, non-sliding) planted directly into the local L1, so
        // the reserve is unambiguously present independent of the Hybrid write path.
        var (cache, l1, _, _) = _CreateCache();
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        var staleValue = Faker.Random.Int(1, 100);
        var options = _FailSafeOptions();
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        await ((IFactoryCacheStore)l1).SetEntryAsync(
            key,
            new CacheStoreEntryWrite<int>
            {
                Value = staleValue,
                IsNull = false,
                LogicalExpiresAt = now.AddMinutes(1),
                PhysicalExpiresAt = now.AddHours(1),
            },
            AbortToken
        );

        // when — a peer broadcasts an Expire invalidation
        var message = new CacheInvalidationMessage
        {
            InstanceId = "instance-2",
            Key = key,
            Expire = true,
        };
        await cache.HandleInvalidationAsync(message, AbortToken);

        // then — the local L1 plain read misses, but the reserve survives and is served by a failing factory
        (await l1.GetAsync<int>(key, AbortToken))
            .HasValue.Should()
            .BeFalse();

        var result = await cache.GetOrAddAsync<int>(
            key,
            _ => throw new InvalidOperationException("upstream unavailable"),
            options,
            AbortToken
        );
        result.HasValue.Should().BeTrue();
        result.Value.Should().Be(staleValue);
        result.IsStale.Should().BeTrue("the Expire receive path preserves the peer L1 reserve");
    }

    [Fact]
    public async Task should_logically_expire_each_peer_l1_key_preserving_reserve_when_receiving_bulk_expire_invalidation()
    {
        // given — two fail-safe entries (Physical > Logical, non-sliding) planted directly into the local L1, so
        // each reserve is unambiguously present. This exercises the Keys[] + Expire=true bulk branch, which loops
        // LocalCache.ExpireAsync per key (logical expiration) rather than RemoveAllAsync (hard removal).
        var (cache, l1, _, _) = _CreateCache();
        await using var _ = cache;

        var key1 = Faker.Random.AlphaNumeric(10);
        var key2 = Faker.Random.AlphaNumeric(10);
        var staleValue1 = Faker.Random.Int(1, 100);
        var staleValue2 = Faker.Random.Int(101, 200);
        var options = _FailSafeOptions();
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        foreach (var (key, staleValue) in new[] { (key1, staleValue1), (key2, staleValue2) })
        {
            await ((IFactoryCacheStore)l1).SetEntryAsync(
                key,
                new CacheStoreEntryWrite<int>
                {
                    Value = staleValue,
                    IsNull = false,
                    LogicalExpiresAt = now.AddMinutes(1),
                    PhysicalExpiresAt = now.AddHours(1),
                },
                AbortToken
            );
        }

        // when — a peer broadcasts a bulk Expire invalidation for both keys (different InstanceId so not self-skipped)
        var message = new CacheInvalidationMessage
        {
            InstanceId = "instance-2",
            Keys = [key1, key2],
            Expire = true,
        };
        await cache.HandleInvalidationAsync(message, AbortToken);

        // then — each key was logically EXPIRED, not hard-removed: plain reads miss, but each fail-safe reserve
        // survives and is served (stale) by a failing factory, with its own original value.
        (await l1.GetAsync<int>(key1, AbortToken))
            .HasValue.Should()
            .BeFalse();
        (await l1.GetAsync<int>(key2, AbortToken)).HasValue.Should().BeFalse();

        var result1 = await cache.GetOrAddAsync<int>(
            key1,
            _ => throw new InvalidOperationException("upstream unavailable"),
            options,
            AbortToken
        );
        result1.HasValue.Should().BeTrue();
        result1.Value.Should().Be(staleValue1);
        result1.IsStale.Should().BeTrue("the bulk Expire receive path preserves each peer L1 reserve");

        var result2 = await cache.GetOrAddAsync<int>(
            key2,
            _ => throw new InvalidOperationException("upstream unavailable"),
            options,
            AbortToken
        );
        result2.HasValue.Should().BeTrue();
        result2.Value.Should().Be(staleValue2);
        result2.IsStale.Should().BeTrue("the bulk Expire receive path preserves each peer L1 reserve");
    }

    [Fact]
    public async Task should_remove_peer_l1_without_reserve_when_receiving_non_expire_invalidation()
    {
        // given — same fail-safe entry planted directly into L1, contrasted against a plain (Remove) invalidation
        var (cache, l1, _, _) = _CreateCache();
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        var options = _FailSafeOptions();
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        await ((IFactoryCacheStore)l1).SetEntryAsync(
            key,
            new CacheStoreEntryWrite<int>
            {
                Value = Faker.Random.Int(1, 100),
                IsNull = false,
                LogicalExpiresAt = now.AddMinutes(1),
                PhysicalExpiresAt = now.AddHours(1),
            },
            AbortToken
        );

        // when — a peer broadcasts a plain (non-Expire) invalidation
        var message = new CacheInvalidationMessage { InstanceId = "instance-2", Key = key };
        await cache.HandleInvalidationAsync(message, AbortToken);

        // then — the entry is removed outright: no reserve remains, a failing factory propagates
        (await l1.GetAsync<int>(key, AbortToken))
            .HasValue.Should()
            .BeFalse();

        var act = async () =>
            await cache.GetOrAddAsync<int>(
                key,
                _ => throw new InvalidOperationException("no reserve"),
                options,
                AbortToken
            );
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("no reserve");
    }

    [Fact]
    public async Task should_return_false_and_publish_nothing_when_expiring_an_absent_key()
    {
        // given
        var (cache, _, _, publisher) = _CreateCache();
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);

        // when
        var expired = await cache.ExpireAsync(key, AbortToken);

        // then
        expired.Should().BeFalse();
        await publisher
            .DidNotReceive()
            .PublishAsync(
                Arg.Any<CacheInvalidationMessage>(),
                Arg.Any<PublishOptions?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_expire_l1_and_publish_when_l2_is_degraded()
    {
        // given — a HybridCache whose L2 starts throwing on writes (degraded mode), wired with auto-recovery
        // so the failing L2 expiration is swallowed + queued instead of bubbling up.
        using var l2 = new TogglableRemoteCache(_timeProvider);
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
        var value = Faker.Random.Int(1, 100);
        await cache.GetOrAddAsync(key, _ => ValueTask.FromResult<int?>(value), TimeSpan.FromMinutes(5), AbortToken);

        // when — L2 is now failing
        l2.FailWrites = true;
        var expired = await cache.ExpireAsync(key, AbortToken);

        // then — conservatively reports true, L1 is expired locally, and an Expire invalidation is still published
        expired.Should().BeTrue();
        (await l1.GetAsync<int>(key, AbortToken)).HasValue.Should().BeFalse();

        await publisher
            .Received(1)
            .PublishAsync(
                Arg.Is<CacheInvalidationMessage>(m => m.Key == key && m.Expire),
                Arg.Any<PublishOptions?>(),
                Arg.Any<CancellationToken>()
            );

        // and — an Expire item was enqueued in the recovery queue (not a Remove or SetEntry)
        cache.RecoveryQueue!.Count.Should().Be(1);
        cache.RecoveryQueue.GetKind(key).Should().Be(HybridCacheRecoveryKind.Expire);
    }

    [Fact]
    public async Task should_replay_expire_against_l2_and_publish_expire_invalidation_on_recovery()
    {
        // given — a HybridCache with auto-recovery; the key exists in both tiers
        using var l2 = new TogglableRemoteCache(_timeProvider);
        using var l1 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions { CloneValues = true });
        var publisher = Substitute.For<IBus>();
        publisher
            .PublishAsync(Arg.Any<CacheInvalidationMessage>(), Arg.Any<PublishOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var recoveryDelay = TimeSpan.FromSeconds(5);
        var cache = new HybridCache(
            l1,
            l2,
            publisher,
            new HybridCacheOptions { EnableAutoRecovery = true, AutoRecoveryDelay = recoveryDelay },
            timeProvider: _timeProvider
        );
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        var value = Faker.Random.Int(1, 100);
        await cache.GetOrAddAsync(key, _ => ValueTask.FromResult<int?>(value), TimeSpan.FromMinutes(5), AbortToken);

        // confirm both tiers have the value before the outage
        (await l2.GetAsync<int>(key, AbortToken))
            .HasValue.Should()
            .BeTrue();

        // when — L2 fails; ExpireAsync queues the expiration for recovery
        l2.FailWrites = true;
        await cache.ExpireAsync(key, AbortToken);

        // then — Expire item is enqueued
        cache.RecoveryQueue!.Count.Should().Be(1);
        cache.RecoveryQueue.GetKind(key).Should().Be(HybridCacheRecoveryKind.Expire);
        var removeAttemptsBefore = l2.RemoveAttempts;

        // when — L2 recovers and the recovery cadence elapses
        l2.FailWrites = false;
        _timeProvider.Advance(recoveryDelay);
        await cache.RecoveryQueue.ProcessAsync(AbortToken);

        // then — the replay called l2.ExpireAsync (RemoveAttempts incremented, not SetEntryAttempts)
        l2.RemoveAttempts.Should().Be(removeAttemptsBefore + 1, "replay must call ExpireAsync on L2, not RemoveAsync");

        // and — the L2 entry is logically gone after the replay
        (await l2.GetAsync<int>(key, AbortToken))
            .HasValue.Should()
            .BeFalse("the replayed ExpireAsync must remove the key from L2");

        // and — a second invalidation was published with Expire=true (not Expire=false / plain remove)
        await publisher
            .Received(2)
            .PublishAsync(
                Arg.Is<CacheInvalidationMessage>(m => m.Key == key && m.Expire),
                Arg.Any<PublishOptions?>(),
                Arg.Any<CancellationToken>()
            );

        // and — the queue drained
        cache.RecoveryQueue.Count.Should().Be(0);
    }
}
