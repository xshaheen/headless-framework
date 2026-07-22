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
        hub.Hit += (_, e) => received = e;

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
    public void should_not_allocate_when_event_has_no_subscriber()
    {
        // given — no subscriber on Hit
        var hub = _CreateHub();

        // when — warm up the JIT, then measure a single fire
        hub.OnHit("warmup", isStale: false);
        var before = GC.GetAllocatedBytesForCurrentThread();
        hub.OnHit("measured", isStale: false);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // then — a fire with no subscriber builds no args and does no work
        allocated.Should().Be(0);
        hub.HasSubscribers.Should().BeFalse();
    }

    [Fact]
    public void should_run_handlers_synchronously_when_sync_handlers_enabled()
    {
        // given
        var hub = _CreateHub(syncHandlers: true);
        var ranOnCallingThread = false;
        var callingThreadId = Environment.CurrentManagedThreadId;
        hub.Set += (_, _) => ranOnCallingThread = Environment.CurrentManagedThreadId == callingThreadId;

        // when
        hub.OnSet("k");

        // then — the handler already ran, inline, on the calling thread
        ranOnCallingThread.Should().BeTrue();
    }

    [Fact]
    public async Task should_run_handlers_on_background_thread_by_default()
    {
        // given — background dispatch (default)
        var hub = _CreateHub(syncHandlers: false);
        using var handlerEntered = new ManualResetEventSlim(false);
        using var releaseHandler = new ManualResetEventSlim(false);
        var completed = new TaskCompletionSource();
        hub.Set += (_, _) =>
        {
            handlerEntered.Set();
            releaseHandler.Wait(TimeSpan.FromSeconds(5));
            completed.TrySetResult();
        };

        // when — OnSet returns without waiting for the (blocked) handler
        hub.OnSet("k");

        // then — the fire did not block on the handler
        handlerEntered.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
        completed.Task.IsCompleted.Should().BeFalse();

        // release and let the background handler finish
        releaseHandler.Set();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
    }

    [Fact]
    public void should_swallow_and_log_sync_handler_exception_without_propagating()
    {
        // given
        var hub = _CreateHub();
        hub.Remove += (_, _) => throw new InvalidOperationException("boom");

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
        hub.Clear += (_, _) => firstRan = true;
        hub.Clear += (_, _) => throw new InvalidOperationException("boom");
        hub.Clear += (_, _) => thirdRan = true;

        // when
        hub.OnClear();

        // then — a throwing handler does not stop the others
        firstRan.Should().BeTrue();
        thirdRan.Should().BeTrue();
    }

    [Fact]
    public void should_reflect_subscription_state_in_has_subscribers()
    {
        // given
        var hub = _CreateHub();
        EventHandler<CacheHitEventArgs> handler = (_, _) => { };

        // then
        hub.HasSubscribers.Should().BeFalse();
        hub.HasEvictionSubscribers.Should().BeFalse();

        hub.Hit += handler;
        hub.HasSubscribers.Should().BeTrue();

        hub.Hit -= handler;
        hub.HasSubscribers.Should().BeFalse();
    }

    [Fact]
    public void should_report_eviction_subscribers_independently()
    {
        // given
        var hub = _CreateHub();
        hub.Miss += (_, _) => { };

        // then — a subscriber on an unrelated event does not report eviction subscribers
        hub.HasSubscribers.Should().BeTrue();
        hub.HasEvictionSubscribers.Should().BeFalse();

        hub.Eviction += (_, _) => { };
        hub.HasEvictionSubscribers.Should().BeTrue();
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
    public void should_carry_tier_on_sub_hub_events()
    {
        // given
        var hub = _CreateHub(withTierSubHubs: true);
        CacheKeyEventArgs? memory = null;
        CacheKeyEventArgs? distributed = null;
        hub.Memory!.Hit += (_, e) => memory = e;
        hub.Distributed!.Miss += (_, e) => distributed = e;

        // when
        hub.MemoryHub!.OnHit("k");
        hub.DistributedHub!.OnMiss("k");

        // then
        memory!.Tier.Should().Be(CacheTier.L1);
        distributed!.Tier.Should().Be(CacheTier.L2);
    }
}
