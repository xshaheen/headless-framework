// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Testing.Tests;

namespace Tests;

public sealed class CacheEventsHubTests : TestBase
{
    private static CacheEventsHub _CreateHub(bool syncHandlers = true, bool withTierSubHubs = false) =>
        new(
            "test-cache",
            CacheTier.L1,
            new CacheEventsConfig { SyncHandlers = syncHandlers },
            logger: null,
            withTierSubHubs
        );

    [Fact]
    public void should_invoke_handler_with_expected_args_when_subscribed()
    {
        // given
        var hub = _CreateHub();
        CacheHitEventArgs? received = null;
        using var _ = hub.Hit.AddHandler((_, e) => received = e);

        // when
        hub.OnHit("k1", isStale: true);

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
        var hub = _CreateHub();
        var completed = new TaskCompletionSource();
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
        var hub = _CreateHub();

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
    public void should_run_synchronous_handlers_inline_when_sync_handlers_enabled()
    {
        // given
        var hub = _CreateHub(syncHandlers: true);
        var ran = false;
        using var _ = hub.Set.AddHandler((_, _) => ran = true);

        // when
        hub.OnSet("k");

        // then — the handler already ran, inline, before OnSet returned
        ran.Should().BeTrue();
    }

    [Fact]
    public async Task should_run_handlers_on_background_task_by_default()
    {
        // given — background dispatch (default)
        var hub = _CreateHub(syncHandlers: false);
        using var handlerEntered = new ManualResetEventSlim(false);
        using var releaseHandler = new ManualResetEventSlim(false);
        var completed = new TaskCompletionSource();
        using var _ = hub.Set.AddHandler(
            (_, _) =>
            {
                handlerEntered.Set();
                releaseHandler.Wait(TimeSpan.FromSeconds(5), AbortToken);
                completed.TrySetResult();
            }
        );

        // when — OnSet returns without waiting for the (blocked) handler
        hub.OnSet("k");

        // then — the fire did not block on the handler
        handlerEntered.Wait(TimeSpan.FromSeconds(5), AbortToken).Should().BeTrue();
        completed.Task.IsCompleted.Should().BeFalse();

        releaseHandler.Set();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
    }

    [Fact]
    public void should_swallow_synchronous_handler_exception_without_propagating()
    {
        // given
        var hub = _CreateHub();
        using var _ = hub.Remove.AddHandler((_, _) => throw new InvalidOperationException("boom"));

        // when
        var act = () => hub.OnRemove("k");

        // then — the exception never reaches the caller
        act.Should().NotThrow();
    }

    [Fact]
    public void should_invoke_all_handlers_when_multiple_subscribed_even_if_one_throws()
    {
        // given
        var hub = _CreateHub();
        var firstRan = false;
        var thirdRan = false;
        using var _1 = hub.Clear.AddHandler((_, _) => firstRan = true);
        using var _2 = hub.Clear.AddHandler((_, _) => throw new InvalidOperationException("boom"));
        using var _3 = hub.Clear.AddHandler((_, _) => thirdRan = true);

        // when
        hub.OnClear();

        // then — a throwing handler does not stop the others (SafeInvokeAsync isolates faults)
        firstRan.Should().BeTrue();
        thirdRan.Should().BeTrue();
    }

    [Fact]
    public void should_reflect_subscription_state_in_has_subscribers()
    {
        // given
        var hub = _CreateHub();
        hub.HasSubscribers.Should().BeFalse();
        hub.HasEvictionSubscribers.Should().BeFalse();

        // when
        var registration = hub.Hit.AddHandler((_, _) => { });

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
        var hub = _CreateHub();
        using var _1 = hub.Miss.AddHandler((_, _) => { });

        // then — a handler on an unrelated event does not report eviction/set subscribers
        hub.HasSubscribers.Should().BeTrue();
        hub.HasEvictionSubscribers.Should().BeFalse();
        hub.HasSetSubscribers.Should().BeFalse();

        using var _2 = hub.Eviction.AddHandler((_, _) => { });
        using var _3 = hub.Set.AddHandler((_, _) => { });
        hub.HasEvictionSubscribers.Should().BeTrue();
        hub.HasSetSubscribers.Should().BeTrue();
    }

    [Fact]
    public void should_expose_tier_sub_hubs_only_when_requested()
    {
        // given / when
        var single = _CreateHub(withTierSubHubs: false);
        var hybrid = _CreateHub(withTierSubHubs: true);

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
        var hub = _CreateHub(withTierSubHubs: true);
        var memory = new TaskCompletionSource<CacheKeyEventArgs>();
        var distributed = new TaskCompletionSource<CacheKeyEventArgs>();
        using var _1 = hub.Memory!.Hit.AddHandler((_, e) => memory.TrySetResult(e));
        using var _2 = hub.Distributed!.Miss.AddHandler((_, e) => distributed.TrySetResult(e));

        // when — tier events always dispatch on a background task
        hub.MemoryHub!.OnHit("k");
        hub.DistributedHub!.OnMiss("k");

        // then
        (await memory.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken))
            .Tier.Should()
            .Be(CacheTier.L1);
        (await distributed.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken)).Tier.Should().Be(CacheTier.L2);
    }
}
