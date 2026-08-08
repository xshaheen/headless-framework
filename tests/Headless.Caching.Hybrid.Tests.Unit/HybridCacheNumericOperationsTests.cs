// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class HybridCacheNumericOperationsTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly List<IDisposable> _disposables = [];

    private (HybridCache cache, InMemoryCache l1, InMemoryCache l2, IBus bus) _CreateCache()
    {
        var l1 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions { CloneValues = true });
        var l2 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions { CloneValues = true });
        var bus = Substitute.For<IBus>();
        bus.PublishAsync(Arg.Any<CacheInvalidationMessage>(), Arg.Any<PublishOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var cache = new HybridCache(
            l1,
            new InMemoryRemoteCacheAdapter(l2),
            bus,
            new HybridCacheOptions(),
            timeProvider: _timeProvider
        );

        _disposables.Add(l1);
        _disposables.Add(l2);
        return (cache, l1, l2, bus);
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        foreach (var disposable in _disposables)
        {
            disposable.Dispose();
        }

        _disposables.Clear();
        await base.DisposeAsyncCore().ConfigureAwait(false);
    }

    [Fact]
    public async Task should_sync_double_increment_result_from_l2_to_l1()
    {
        var (cache, l1, l2, bus) = _CreateCache();
        await using var _ = cache;
        const string key = "counter";
        var expiration = TimeSpan.FromMinutes(5);
        await l1.UpsertAsync(key, 99d, expiration, AbortToken);
        await l2.UpsertAsync(key, 1.5d, expiration, AbortToken);

        var result = await cache.IncrementAsync(key, 0.5d, expiration, AbortToken);

        result.Should().Be(2d);
        (await l1.GetAsync<double>(key, AbortToken)).Value.Should().Be(2d);
        (await l2.GetAsync<double>(key, AbortToken)).Value.Should().Be(2d);
        await bus.Received(1)
            .PublishAsync(
                Arg.Is<CacheInvalidationMessage>(message => message.Key == key),
                Arg.Any<PublishOptions?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_sync_higher_value_to_l1_when_l2_changes()
    {
        var (cache, l1, l2, _) = _CreateCache();
        await using var _ = cache;
        const string key = "high-watermark";
        var expiration = TimeSpan.FromMinutes(5);
        await l2.UpsertAsync(key, 5L, expiration, AbortToken);

        var difference = await cache.SetIfHigherAsync(key, 10L, expiration, AbortToken);

        difference.Should().Be(5L);
        (await l1.GetAsync<long>(key, AbortToken)).Value.Should().Be(10L);
        (await l2.GetAsync<long>(key, AbortToken)).Value.Should().Be(10L);
    }

    [Fact]
    public async Task should_evict_unknown_l1_value_when_set_if_higher_does_not_change_l2()
    {
        var (cache, l1, l2, _) = _CreateCache();
        await using var _ = cache;
        const string key = "high-watermark";
        var expiration = TimeSpan.FromMinutes(5);
        await l1.UpsertAsync(key, 1d, expiration, AbortToken);
        await l2.UpsertAsync(key, 10d, expiration, AbortToken);

        var difference = await cache.SetIfHigherAsync(key, 9d, expiration, AbortToken);

        difference.Should().Be(0d);
        (await l1.GetAsync<double>(key, AbortToken)).HasValue.Should().BeFalse();
        (await l2.GetAsync<double>(key, AbortToken)).Value.Should().Be(10d);
    }

    [Fact]
    public async Task should_sync_lower_value_to_l1_when_l2_changes()
    {
        var (cache, l1, l2, _) = _CreateCache();
        await using var _ = cache;
        const string key = "low-watermark";
        var expiration = TimeSpan.FromMinutes(5);
        await l2.UpsertAsync(key, 10d, expiration, AbortToken);

        var difference = await cache.SetIfLowerAsync(key, 4d, expiration, AbortToken);

        difference.Should().Be(6d);
        (await l1.GetAsync<double>(key, AbortToken)).Value.Should().Be(4d);
        (await l2.GetAsync<double>(key, AbortToken)).Value.Should().Be(4d);
    }

    [Fact]
    public async Task should_evict_unknown_l1_value_when_set_if_lower_does_not_change_l2()
    {
        var (cache, l1, l2, _) = _CreateCache();
        await using var _ = cache;
        const string key = "low-watermark";
        var expiration = TimeSpan.FromMinutes(5);
        await l1.UpsertAsync(key, 99L, expiration, AbortToken);
        await l2.UpsertAsync(key, 4L, expiration, AbortToken);

        var difference = await cache.SetIfLowerAsync(key, 5L, expiration, AbortToken);

        difference.Should().Be(0L);
        (await l1.GetAsync<long>(key, AbortToken)).HasValue.Should().BeFalse();
        (await l2.GetAsync<long>(key, AbortToken)).Value.Should().Be(4L);
    }

    [Theory]
    [InlineData(NumericOperation.IncrementDouble)]
    [InlineData(NumericOperation.IncrementLong)]
    [InlineData(NumericOperation.HigherDouble)]
    [InlineData(NumericOperation.HigherLong)]
    [InlineData(NumericOperation.LowerDouble)]
    [InlineData(NumericOperation.LowerLong)]
    public async Task should_remove_existing_value_when_numeric_expiration_is_zero(NumericOperation operation)
    {
        var (cache, l1, l2, _) = _CreateCache();
        await using var _ = cache;
        var key = $"expired-{operation}";
        var expiration = TimeSpan.FromMinutes(5);
        await l1.UpsertAsync(key, 10d, expiration, AbortToken);
        await l2.UpsertAsync(key, 10d, expiration, AbortToken);

        var result = operation switch
        {
            NumericOperation.IncrementDouble => await cache.IncrementAsync(key, 1d, TimeSpan.Zero, AbortToken),
            NumericOperation.IncrementLong => await cache.IncrementAsync(key, 1L, TimeSpan.Zero, AbortToken),
            NumericOperation.HigherDouble => await cache.SetIfHigherAsync(key, 11d, TimeSpan.Zero, AbortToken),
            NumericOperation.HigherLong => await cache.SetIfHigherAsync(key, 11L, TimeSpan.Zero, AbortToken),
            NumericOperation.LowerDouble => await cache.SetIfLowerAsync(key, 9d, TimeSpan.Zero, AbortToken),
            NumericOperation.LowerLong => await cache.SetIfLowerAsync(key, 9L, TimeSpan.Zero, AbortToken),
            _ => throw new InvalidOperationException($"Unknown operation: {operation}"),
        };

        result.Should().Be(0d);
        (await l1.ExistsAsync(key, AbortToken)).Should().BeFalse();
        (await l2.ExistsAsync(key, AbortToken)).Should().BeFalse();
    }

    [Fact]
    public async Task should_wipe_l1_and_not_queue_non_replay_safe_numeric_failure()
    {
        var l1 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions { CloneValues = true });
        var remote = new TogglableRemoteCache(_timeProvider);
        var bus = Substitute.For<IBus>();
        bus.PublishAsync(Arg.Any<CacheInvalidationMessage>(), Arg.Any<PublishOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var cache = new HybridCache(
            l1,
            remote,
            bus,
            new HybridCacheOptions { EnableAutoRecovery = true },
            timeProvider: _timeProvider
        );
        await using var _ = cache;
        _disposables.Add(l1);
        _disposables.Add(remote);
        const string key = "counter";
        await l1.UpsertAsync(key, 10d, TimeSpan.FromMinutes(5), AbortToken);
        remote.FailWrites = true;

        var act = async () => await cache.SetIfHigherAsync(key, 11d, TimeSpan.FromMinutes(5), AbortToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await l1.ExistsAsync(key, AbortToken)).Should().BeFalse();
        cache.RecoveryQueue!.Count.Should().Be(0, "numeric deltas cannot be replayed safely");
    }

    public enum NumericOperation
    {
        IncrementDouble = 0,
        IncrementLong = 1,
        HigherDouble = 2,
        HigherLong = 3,
        LowerDouble = 4,
        LowerLong = 5,
    }
}
