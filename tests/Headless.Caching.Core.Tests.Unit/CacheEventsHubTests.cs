// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Primitives;
using Headless.Testing.Tests;

namespace Tests;

public sealed class CacheEventsHubTests : TestBase
{
    private static CacheEventsHub _CreateHub(int bufferCapacity = 2_048, bool withTierSubHubs = false) =>
        new(
            "test-cache",
            CacheTier.L1,
            new CacheEventsConfig { BufferCapacity = bufferCapacity, ShutdownDrainTimeout = TimeSpan.FromSeconds(1) },
            logger: null,
            withTierSubHubs
        );

    [Fact]
    public async Task should_invoke_handler_with_expected_args_when_subscribed()
    {
        // given
        await using var hub = _CreateHub();
        CacheHitEventArgs? received = null;
        using var _ = hub.Hit.AddHandler(e => received = e);

        // when
        hub.OnHit("k1", isStale: true);
        await hub.DrainAsync(AbortToken);

        // then
        received.Should().NotBeNull();
        received!.CacheName.Should().Be("test-cache");
        received.Tier.Should().Be(CacheTier.L1);
        received.Key.Should().Be("k1");
        received.IsStale.Should().BeTrue();
    }

    [Fact]
    public async Task should_invoke_asynchronous_handler()
    {
        // given
        await using var hub = _CreateHub();
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var _ = hub.Set.AddHandler(
            async (_, _, ct) =>
            {
                await Task.Yield();
                completed.TrySetResult();
            }
        );

        // when
        hub.OnSet("k");

        // then
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
    }

    [Fact]
    public void should_not_allocate_when_event_has_no_subscriber()
    {
        // given — no handler on Hit
        using var hub = _CreateHub();

        // when — warm up the JIT, then measure a single fire
        hub.OnHit("warmup", isStale: false);
        var before = GC.GetAllocatedBytesForCurrentThread();
        hub.OnHit("measured", isStale: false);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // then — a fire with no handler builds no args and does no work
        allocated.Should().Be(0);
        hub.HasSubscribers.Should().BeFalse();
    }

    [Fact]
    public async Task should_not_block_the_producer_on_a_slow_handler()
    {
        // given
        await using var hub = _CreateHub();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var _ = hub.Set.AddHandler(
            async (_, ct) =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(ct);
            }
        );

        // when
        hub.OnSet("k");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // then — the emitter returned while its handler is still suspended
        release.Task.IsCompleted.Should().BeFalse();
        hub.DispatchStatistics.Pending.Should().Be(1);

        release.TrySetResult();
        await hub.DrainAsync(AbortToken);
    }

    [Fact]
    public async Task should_swallow_synchronous_handler_exception_without_propagating()
    {
        // given
        await using var hub = _CreateHub();
        using var _ = hub.Remove.AddHandler(_ => throw new InvalidOperationException("boom"));

        // when
        var act = () => hub.OnRemove("k");

        // then — the exception never reaches the caller
        act.Should().NotThrow();
        await hub.DrainAsync(AbortToken);
    }

    [Fact]
    public async Task should_invoke_all_handlers_when_multiple_subscribed_even_if_one_throws()
    {
        // given
        await using var hub = _CreateHub();
        var firstRan = false;
        var thirdRan = false;
        using var _1 = hub.Clear.AddHandler(_ => firstRan = true);
        using var _2 = hub.Clear.AddHandler(_ => throw new InvalidOperationException("boom"));
        using var _3 = hub.Clear.AddHandler(_ => thirdRan = true);

        // when
        hub.OnClear();
        await hub.DrainAsync(AbortToken);

        // then — a throwing handler does not stop the others (SafeInvokeAsync isolates faults)
        firstRan.Should().BeTrue();
        thirdRan.Should().BeTrue();
    }

    [Fact]
    public void should_reflect_subscription_state_in_has_subscribers()
    {
        // given
        using var hub = _CreateHub();
        hub.HasSubscribers.Should().BeFalse();
        hub.HasEvictionSubscribers.Should().BeFalse();

        // when
        var registration = hub.Hit.AddHandler(_ => { });

        // then
        hub.HasSubscribers.Should().BeTrue();

        // and disposing the registration unsubscribes
        registration.Dispose();
        hub.HasSubscribers.Should().BeFalse();
    }

    [Fact]
    public void should_report_specific_subscriber_flags_independently()
    {
        // given
        using var hub = _CreateHub();
        using var _1 = hub.Miss.AddHandler(_ => { });

        // then — a handler on an unrelated event does not report eviction/set subscribers
        hub.HasSubscribers.Should().BeTrue();
        hub.HasEvictionSubscribers.Should().BeFalse();
        hub.HasSetSubscribers.Should().BeFalse();

        using var _2 = hub.Eviction.AddHandler(_ => { });
        using var _3 = hub.Set.AddHandler(_ => { });
        hub.HasEvictionSubscribers.Should().BeTrue();
        hub.HasSetSubscribers.Should().BeTrue();
    }

    [Fact]
    public void should_expose_tier_sub_hubs_only_when_requested()
    {
        // given / when
        using var single = _CreateHub(withTierSubHubs: false);
        using var hybrid = _CreateHub(withTierSubHubs: true);

        // then
        single.Memory.Should().BeNull();
        single.Distributed.Should().BeNull();
        hybrid.Memory.Should().NotBeNull();
        hybrid.Distributed.Should().NotBeNull();
    }

    [Fact]
    public async Task should_carry_tier_on_sub_hub_events()
    {
        // given
        await using var hub = _CreateHub(withTierSubHubs: true);
        var memory = new TaskCompletionSource<CacheKeyEventArgs>();
        var distributed = new TaskCompletionSource<CacheKeyEventArgs>();
        using var _1 = hub.Memory!.Hit.AddHandler(e => memory.TrySetResult(e));
        using var _2 = hub.Distributed!.Miss.AddHandler(e => distributed.TrySetResult(e));

        // when
        hub.MemoryHub!.OnHit("k");
        hub.DistributedHub!.OnMiss("k");

        // then
        (await memory.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken))
            .Tier.Should()
            .Be(CacheTier.L1);
        (await distributed.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken)).Tier.Should().Be(CacheTier.L2);
    }

    [Fact]
    public async Task should_preserve_fifo_across_root_and_tier_events()
    {
        // given
        await using var hub = _CreateHub(withTierSubHubs: true);
        var observed = new List<string>();
        using var _1 = hub.Set.AddHandler(e => observed.Add($"set:{e.Key}"));
        using var _2 = hub.Memory!.Hit.AddHandler(e => observed.Add($"l1-hit:{e.Key}"));
        using var _3 = hub.Miss.AddHandler(e => observed.Add($"miss:{e.Key}"));

        // when
        hub.OnSet("1");
        hub.MemoryHub!.OnHit("2");
        hub.OnMiss("3");
        await hub.DrainAsync(AbortToken);

        // then
        observed.Should().Equal("set:1", "l1-hit:2", "miss:3");
    }

    [Fact]
    public async Task should_invoke_the_handler_snapshot_captured_at_emission()
    {
        // given
        await using var hub = _CreateHub();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldHandlerKeys = new List<string>();
        var newHandlerKeys = new List<string>();
        var oldRegistration = hub.Set.AddHandler(
            async (args, ct) =>
            {
                oldHandlerKeys.Add(args.Key);

                if (string.Equals(args.Key, "blocking", StringComparison.Ordinal))
                {
                    entered.TrySetResult();
                    await release.Task.WaitAsync(ct);
                }
            }
        );

        hub.OnSet("blocking");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // when — queued captures belong to the old registration even if subscriptions change before dispatch
        hub.OnSet("captured");
        oldRegistration.Dispose();
        using var newRegistration = hub.Set.AddHandler(args => newHandlerKeys.Add(args.Key));
        release.TrySetResult();
        await hub.DrainAsync(AbortToken);

        hub.OnSet("new");
        await hub.DrainAsync(AbortToken);

        // then
        oldHandlerKeys.Should().Equal("blocking", "captured");
        newHandlerKeys.Should().Equal("new");
    }

    [Fact]
    public async Task should_drop_the_newest_signal_when_the_fifo_is_full()
    {
        // given
        await using var hub = _CreateHub(bufferCapacity: 1);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var keys = new List<string>();
        using var _ = hub.Set.AddHandler(
            async (args, ct) =>
            {
                keys.Add(args.Key);

                if (string.Equals(args.Key, "active", StringComparison.Ordinal))
                {
                    entered.TrySetResult();
                    await release.Task.WaitAsync(ct);
                }
            }
        );

        hub.OnSet("active");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // when — one signal waits behind the active handler; the next cannot enter the capacity-one FIFO
        hub.OnSet("buffered");
        hub.OnSet("dropped");
        release.TrySetResult();
        await hub.DrainAsync(AbortToken);

        // then
        keys.Should().Equal("active", "buffered");
        hub.DispatchStatistics.Should()
            .Be(new CacheEventDispatchStatistics(Accepted: 2, Processed: 2, Dropped: 1, Pending: 0, Capacity: 1));
    }

    [Fact]
    public async Task should_drain_accepted_signals_during_disposal()
    {
        // given
        var hub = _CreateHub();
        var keys = new List<string>();
        using var _ = hub.Set.AddHandler(args => keys.Add(args.Key));
        hub.OnSet("one");
        hub.OnSet("two");

        // when
        await hub.DisposeAsync();

        // then
        keys.Should().Equal("one", "two");
        hub.DispatchStatistics.Pending.Should().Be(0);
    }

    [Fact]
    public async Task should_cancel_a_handler_after_the_shutdown_drain_timeout()
    {
        // given
        await using var hub = new CacheEventsHub(
            "test-cache",
            CacheTier.L1,
            new CacheEventsConfig { ShutdownDrainTimeout = TimeSpan.Zero }
        );
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var _ = hub.Set.AddHandler(
            async (_, ct) =>
            {
                entered.TrySetResult();

                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    canceled.TrySetResult();
                    throw;
                }
            }
        );
        hub.OnSet("active");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // when
        await hub.DisposeAsync();

        // then
        await canceled.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        await hub.DrainAsync(AbortToken);
        hub.DispatchStatistics.Pending.Should().Be(0);
    }

    [Fact]
    public void should_reject_invalid_buffer_configuration()
    {
        // when
        var act = () => new CacheEventsHub("test-cache", CacheTier.L1, new CacheEventsConfig { BufferCapacity = 0 });

        // then
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void no_op_event_should_validate_subscription_callbacks()
    {
        // given
        var noOp = CacheEvents.NoOp.Hit;

        // when / then
        FluentActions
            .Invoking(() => noOp.AddHandler((Action<CacheHitEventArgs>)null!))
            .Should()
            .Throw<ArgumentNullException>();
        FluentActions
            .Invoking(() => noOp.AddHandler((Func<CacheHitEventArgs, CancellationToken, ValueTask>)null!))
            .Should()
            .Throw<ArgumentNullException>();
        FluentActions
            .Invoking(() => noOp.AddHandler((AsyncEventHandler<CacheHitEventArgs>)null!))
            .Should()
            .Throw<ArgumentNullException>();
        FluentActions.Invoking(() => noOp.Subscribe(null!)).Should().Throw<ArgumentNullException>();
        FluentActions
            .Invoking(() =>
                noOp.SafeInvokeAsync(this, new CacheHitEventArgs("test", CacheTier.L1, "key", isStale: false), null!)
            )
            .Should()
            .Throw<ArgumentNullException>();
    }
}
