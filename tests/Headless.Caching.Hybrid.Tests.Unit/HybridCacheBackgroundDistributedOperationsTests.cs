// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class HybridCacheBackgroundDistributedOperationsTests : TestBase
{
    private static readonly TimeSpan _Delay = TimeSpan.FromSeconds(5);

    private readonly FakeTimeProvider _timeProvider = new();

    private static IBus _CreateNoopPublisher()
    {
        var publisher = Substitute.For<IBus>();
        publisher
            .PublishAsync(Arg.Any<CacheInvalidationMessage>(), Arg.Any<PublishOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        return publisher;
    }

    private (HybridCache cache, InMemoryCache l1, GatedRemoteCache l2) _CreateGatedCache(HybridCacheOptions options)
    {
        var l1 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions { CloneValues = true });
        var l2 = new GatedRemoteCache(_timeProvider);
        var cache = new HybridCache(l1, l2, _CreateNoopPublisher(), options, timeProvider: _timeProvider);
        return (cache, l1, l2);
    }

    private (HybridCache cache, InMemoryCache l1, TogglableRemoteCache l2, IBus publisher) _CreateTogglableCache(
        HybridCacheOptions options
    )
    {
        var l1 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions { CloneValues = true });
        var l2 = new TogglableRemoteCache(_timeProvider);
        var publisher = _CreateNoopPublisher();
        var cache = new HybridCache(l1, l2, publisher, options, timeProvider: _timeProvider);
        return (cache, l1, l2, publisher);
    }

    /// <summary>Spins until the predicate holds or the real-time budget elapses, polling the background tail.</summary>
    private static async Task _WaitUntilAsync(Func<ValueTask<bool>> predicate)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);

        while (DateTime.UtcNow < deadline)
        {
            if (await predicate())
            {
                return;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException("Background L2 operation did not complete within the budget.");
    }

    [Fact]
    public async Task should_return_before_gated_l2_write_when_flag_on()
    {
        // given — background distributed ops on, the L2 scalar write parked behind a gate
        var (cache, l1, l2) = _CreateGatedCache(
            new HybridCacheOptions { AllowBackgroundDistributedCacheOperations = true }
        );
        await using var _ = cache;

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        l2.UpsertGate = gate;
        var key = Faker.Random.AlphaNumeric(10);
        var value = Faker.Random.Int();

        // when — the caller upserts; the L2 write detaches and parks on the gate
        var updated = await cache.UpsertAsync(key, value, TimeSpan.FromMinutes(5), AbortToken);

        // then — the caller already returned and L1 is populated, while the L2 write is still parked (not done)
        updated.Should().BeTrue();
        (await l1.GetAsync<int>(key, AbortToken)).Value.Should().Be(value);
        await l2.UpsertStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        (await l2.GetAsync<int>(key, AbortToken)).HasValue.Should().BeFalse("the gated L2 write has not run yet");

        // when — the gate is released the background write lands the value in L2
        gate.SetResult();
        await _WaitUntilAsync(async () => (await l2.GetAsync<int>(key, AbortToken)).HasValue);

        // then
        (await l2.GetAsync<int>(key, AbortToken))
            .Value.Should()
            .Be(value);
    }

    [Fact]
    public async Task should_return_before_gated_l2_write_for_factory_path_when_flag_on()
    {
        // given — background ops on, the factory store write (SetEntryAsync) parked behind the write gate
        var (cache, l1, l2) = _CreateGatedCache(
            new HybridCacheOptions { AllowBackgroundDistributedCacheOperations = true }
        );
        await using var _ = cache;

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        l2.WriteGate = gate;
        var key = Faker.Random.AlphaNumeric(10);
        var value = Faker.Random.Int();

        // when — GetOrAdd misses both tiers, runs the factory, writes L1 sync, and detaches the L2 write-through
        var result = await cache.GetOrAddAsync(
            key,
            _ => new ValueTask<int?>(value),
            TimeSpan.FromMinutes(5),
            AbortToken
        );

        // then — the caller got the factory value and L1 holds it, while the L2 write is still parked
        result.Value.Should().Be(value);
        (await l1.GetAsync<int>(key, AbortToken)).Value.Should().Be(value);
        await l2.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        (await l2.GetAsync<int>(key, AbortToken)).HasValue.Should().BeFalse("the gated L2 write has not run yet");

        // when — releasing the gate lets the background write-through land in L2
        gate.SetResult();
        await _WaitUntilAsync(async () => (await l2.GetAsync<int>(key, AbortToken)).HasValue);

        // then
        (await l2.GetAsync<int>(key, AbortToken))
            .Value.Should()
            .Be(value);
    }

    [Fact]
    public async Task should_return_before_gated_l2_write_for_bulk_path_when_flag_on()
    {
        // given — background ops on, the bulk L2 write parked behind the upsert gate
        var (cache, l1, l2) = _CreateGatedCache(
            new HybridCacheOptions { AllowBackgroundDistributedCacheOperations = true }
        );
        await using var _ = cache;

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        l2.UpsertGate = gate;
        var values = new Dictionary<string, int>(StringComparer.Ordinal) { ["a"] = 1, ["b"] = 2 };

        // when
        var count = await cache.UpsertAllAsync(values, TimeSpan.FromMinutes(5), AbortToken);

        // then — the caller reports the full count and L1 holds every entry while the L2 bulk write is parked
        count.Should().Be(2);
        (await l1.GetAsync<int>("a", AbortToken)).Value.Should().Be(1);
        (await l1.GetAsync<int>("b", AbortToken)).Value.Should().Be(2);
        await l2.UpsertStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        (await l2.GetAsync<int>("a", AbortToken)).HasValue.Should().BeFalse("the gated bulk write has not run yet");

        // when — releasing the gate lets the background bulk write land
        gate.SetResult();
        await _WaitUntilAsync(async () => (await l2.GetAsync<int>("a", AbortToken)).HasValue);

        // then
        (await l2.GetAsync<int>("b", AbortToken))
            .Value.Should()
            .Be(2);
    }

    [Fact]
    public async Task should_queue_failed_background_write_for_replay_when_auto_recovery_on()
    {
        // given — background ops AND auto-recovery on; L2 writes fail
        var (cache, l1, l2, publisher) = _CreateTogglableCache(
            new HybridCacheOptions { AllowBackgroundDistributedCacheOperations = true, EnableAutoRecovery = true }
        );
        await using var _ = cache;

        l2.FailWrites = true;
        var key = Faker.Random.AlphaNumeric(10);
        var value = Faker.Random.Int();

        // when — the caller succeeds against L1; the background L2 write fails and routes to the recovery queue
        var updated = await cache.UpsertAsync(key, value, TimeSpan.FromMinutes(5), AbortToken);

        // then — the caller succeeded and L1 has the value; the failed background write was queued
        updated.Should().BeTrue();
        (await l1.GetAsync<int>(key, AbortToken)).Value.Should().Be(value);
        await _WaitUntilAsync(() => new ValueTask<bool>(cache.RecoveryQueue!.Count == 1));
        (await l2.GetAsync<int>(key, AbortToken)).HasValue.Should().BeFalse();

        // when — L2 recovers and the replay pass runs
        l2.FailWrites = false;
        _timeProvider.Advance(_Delay);
        await cache.RecoveryQueue!.ProcessAsync(AbortToken);

        // then — the replay landed the value in L2 and the queue drained
        (await l2.GetAsync<int>(key, AbortToken))
            .Value.Should()
            .Be(value);
        cache.RecoveryQueue.Count.Should().Be(0);
        await publisher
            .Received()
            .PublishAsync(
                Arg.Is<CacheInvalidationMessage>(m => m.Key == key),
                Arg.Any<PublishOptions?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_succeed_and_log_when_background_write_fails_and_auto_recovery_off()
    {
        // given — background ops on, auto-recovery OFF; L2 writes fail
        var (cache, l1, l2, _) = _CreateTogglableCache(
            new HybridCacheOptions { AllowBackgroundDistributedCacheOperations = true }
        );
        await using var _ = cache;

        l2.FailWrites = true;
        var key = Faker.Random.AlphaNumeric(10);
        var value = Faker.Random.Int();

        // when — the caller still succeeds (fire-and-forget); the background failure is best-effort
        var updated = await cache.UpsertAsync(key, value, TimeSpan.FromMinutes(5), AbortToken);

        // then — caller succeeded, L1 has the value, no recovery queue exists, no unobserved exception thrown
        updated.Should().BeTrue();
        (await l1.GetAsync<int>(key, AbortToken)).Value.Should().Be(value);
        cache.RecoveryQueue.Should().BeNull();

        // and — let the background tail run to completion so a swallowed-vs-unobserved difference would surface
        await _WaitUntilAsync(() => new ValueTask<bool>(l2.UpsertAttempts > 0));
        l2.UpsertAttempts.Should().BePositive("the background write was attempted against L2");
    }

    [Fact]
    public async Task should_await_l2_and_surface_failure_when_flag_off()
    {
        // given — flag OFF (default), auto-recovery off; L2 writes fail
        var (cache, _, l2, _) = _CreateTogglableCache(new HybridCacheOptions());
        await using var _ = cache;

        l2.FailWrites = true;
        var key = Faker.Random.AlphaNumeric(10);

        // when
        var act = async () => await cache.UpsertAsync(key, Faker.Random.Int(), TimeSpan.FromMinutes(5), AbortToken);

        // then — exactly today's behavior: the awaited L2 failure surfaces to the caller
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task should_keep_remove_synchronous_when_flag_on()
    {
        // given — background ops on, but Remove must stay synchronous (its result depends on L2)
        var (cache, l1, l2, _) = _CreateTogglableCache(
            new HybridCacheOptions { AllowBackgroundDistributedCacheOperations = true }
        );
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        var value = Faker.Random.Int();
        await l1.UpsertAsync(key, value, TimeSpan.FromMinutes(5), AbortToken);
        await l2.UpsertAsync(key, value, TimeSpan.FromMinutes(5), AbortToken);

        // when — the L2 remove fails synchronously; with no auto-recovery the failure surfaces (unchanged path)
        l2.FailWrites = true;
        var act = async () => await cache.RemoveAsync(key, AbortToken);

        // then — Remove was NOT backgrounded: the synchronous L2 failure propagates as today
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task should_return_before_gated_backplane_publish_when_flag_on()
    {
        // given — background distributed ops on; the BACKPLANE publish (not just the L2 write) is parked behind a
        // gate. This pins the publish-timing half of the background tail distinctly from the L2-write timing the
        // other tests cover: when the flag is on, the invalidation broadcast runs in the detached tail, so the
        // caller must not block on it (FusionCache CanExecuteBackgroundBackplaneOperations analog — our framework
        // backgrounds the publish together with the L2 write under the single flag rather than a separate one).
        using var l1 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions { CloneValues = true });
        var l2 = new InMemoryRemoteCacheAdapter(
            new InMemoryCache(_timeProvider, new InMemoryCacheOptions { CloneValues = true })
        );

        var publishGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var publishStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var publisher = Substitute.For<IBus>();
        publisher
            .PublishAsync(Arg.Any<CacheInvalidationMessage>(), Arg.Any<PublishOptions?>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                publishStarted.TrySetResult();
                await publishGate.Task;
            });

        var cache = new HybridCache(
            l1,
            l2,
            publisher,
            new HybridCacheOptions { AllowBackgroundDistributedCacheOperations = true },
            timeProvider: _timeProvider
        );
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        var value = Faker.Random.Int();

        // when — the caller upserts; the L2 write and the publish both detach into the background tail
        var updated = await cache.UpsertAsync(key, value, TimeSpan.FromMinutes(5), AbortToken);

        // then — the caller already returned true and L1 holds the value while the backplane publish is still
        // parked on the gate (the caller did not block on it). Awaiting UpsertAsync and observing the value
        // before releasing the gate is the proof that the publish was detached, not inline.
        updated.Should().BeTrue();
        (await l1.GetAsync<int>(key, AbortToken)).Value.Should().Be(value);
        await publishStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        publishGate.Task.IsCompleted.Should().BeFalse("the caller returned before the gated publish completed");

        // when — releasing the gate lets the background publish complete
        publishGate.SetResult();
        await _WaitUntilAsync(() => new ValueTask<bool>(publishGate.Task.IsCompleted));

        // then — exactly one key invalidation was broadcast from the background tail
        await publisher
            .Received(1)
            .PublishAsync(
                Arg.Is<CacheInvalidationMessage>(m => m.Key == key),
                Arg.Any<PublishOptions?>(),
                Arg.Any<CancellationToken>()
            );
    }
}
