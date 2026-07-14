// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Testing.Tests;

namespace Tests;

/// <summary>
/// Cross-provider conformance for composite acquisition over <see cref="IDistributedSemaphoreProvider"/>
/// (<c>TryAcquireAllAsync</c> / <c>AcquireAllAsync</c>) against a real backend.
/// </summary>
/// <remarks>
/// <para>
/// This sits one altitude above <see cref="DistributedSemaphoreStorageTestsBase"/>: that base pins the
/// <see cref="IDistributedSemaphoreStorage"/> slot primitives, this one pins the provider-level all-or-nothing set
/// semantics (canonical order, dedupe, conflicting-capacity rejection, rollback, single-resource passthrough).
/// </para>
/// <para>
/// The provider hook exists because <c>DistributedSemaphoreProvider</c> is <c>internal</c> to
/// <c>Headless.DistributedLocks.Core</c> and this harness package is deliberately outside its
/// <c>InternalsVisibleTo</c> set. Each leaf integration project has internals access and supplies the provider.
/// </para>
/// </remarks>
public abstract class DistributedSemaphoreProviderTestsBase : TestBase
{
    protected abstract IDistributedSemaphoreProvider GetSemaphoreProvider(DistributedLockOptions? options = null);
    protected abstract TimeProvider TimeProvider { get; }
    protected abstract Task AdvanceTimeAsync(TimeSpan amount, CancellationToken cancellationToken);

    /// <summary>
    /// Drives <paramref name="pending"/> to completion when it is blocked on the provider's TimeProvider-driven
    /// backoff — the only safe way to await a composite that must wait for another holder to release. A fake clock
    /// does not advance on its own, so a naive await would hang the fake-clock leaf instead of failing it; the
    /// wall-clock leaf simply waits real time and absorbs the extra advance. The bounded <c>WaitAsync</c> turns a
    /// genuine deadlock into a test failure rather than a hang.
    /// </summary>
    protected async Task<T> PumpUntilCompletedAsync<T>(Task<T> pending, TimeSpan step)
    {
        for (var i = 0; i < 20 && !pending.IsCompleted; i++)
        {
            await AdvanceTimeAsync(step, AbortToken);
            await Task.Yield();
        }

        return await pending.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);
    }

    /// <summary>Covers SC2: one slot of each named semaphore, whatever their individual capacities.</summary>
    public virtual async Task should_acquire_composite_slots_across_differently_sized_semaphores()
    {
        var provider = GetSemaphoreProvider();
        var (first, second) = CompositeTestResources.CreatePair();

        var handle = await provider.AcquireAllAsync(
            [new DistributedSemaphoreRequest(first, 5), new DistributedSemaphoreRequest(second, 2)],
            cancellationToken: AbortToken
        );

        try
        {
            (await provider.GetHolderCountAsync(first, AbortToken)).Should().Be(1);
            (await provider.GetHolderCountAsync(second, AbortToken)).Should().Be(1);
            handle.Resource.Should().Be($"{first}+{second}");
            handle.FencingToken.Should().BeNull();
        }
        finally
        {
            await handle.DisposeAsync();
        }

        (await provider.GetHolderCountAsync(first, AbortToken)).Should().Be(0);
        (await provider.GetHolderCountAsync(second, AbortToken)).Should().Be(0);
    }

    /// <summary>
    /// Covers SC3: maxCount is a property of the semaphore, not of the acquisition, so one resource cannot carry two
    /// capacities. The set is rejected before any semaphore is created, so nothing is acquired.
    /// </summary>
    public virtual async Task should_reject_conflicting_max_count_for_one_resource()
    {
        var provider = GetSemaphoreProvider();
        var (first, _) = CompositeTestResources.CreatePair();

        var act = async () =>
            await provider.TryAcquireAllAsync(
                [new DistributedSemaphoreRequest(first, 5), new DistributedSemaphoreRequest(first, 3)],
                cancellationToken: AbortToken
            );

        (await act.Should().ThrowAsync<ArgumentException>()).And.Message.Should().Contain(first);
        (await provider.GetHolderCountAsync(first, AbortToken)).Should().Be(0);
    }

    /// <summary>
    /// Out-of-order and duplicated input collapses to the canonical ordinal set: one slot per distinct resource, and
    /// the composite identity is the plain ordinal join.
    /// </summary>
    public virtual async Task should_acquire_composite_slots_in_canonical_order_and_deduplicate()
    {
        var provider = GetSemaphoreProvider();
        var (first, second) = CompositeTestResources.CreatePair();

        var handle = await provider.AcquireAllAsync(
            [
                new DistributedSemaphoreRequest(second, 2),
                new DistributedSemaphoreRequest(first, 5),
                new DistributedSemaphoreRequest(second, 2),
            ],
            cancellationToken: AbortToken
        );

        try
        {
            handle.Resource.Should().Be($"{first}+{second}");

            // A duplicate request collapses to a single child; it does not take two permits.
            (await provider.GetHolderCountAsync(first, AbortToken))
                .Should()
                .Be(1);
            (await provider.GetHolderCountAsync(second, AbortToken)).Should().Be(1);
        }
        finally
        {
            await handle.DisposeAsync();
        }
    }

    /// <summary>
    /// Rollback proof by observable effect: the earlier slot is taken, the later semaphore is saturated, and the
    /// failed composite leaves no residue behind — neither its own slot nor the blocker's.
    /// </summary>
    public virtual async Task should_release_earlier_slots_when_later_semaphore_is_saturated()
    {
        var provider = GetSemaphoreProvider();
        var (first, second) = CompositeTestResources.CreatePair();
        var blocker = await provider.CreateSemaphore(second, maxCount: 1).AcquireAsync(cancellationToken: AbortToken);

        try
        {
            var result = await provider.TryAcquireAllAsync(
                [new DistributedSemaphoreRequest(first, 5), new DistributedSemaphoreRequest(second, 1)],
                new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.Zero },
                AbortToken
            );

            result.Should().BeNull();
            (await provider.GetHolderCountAsync(first, AbortToken)).Should().Be(0);
            (await provider.GetHolderCountAsync(second, AbortToken)).Should().Be(1);
        }
        finally
        {
            await blocker.DisposeAsync();
        }

        (await provider.GetHolderCountAsync(second, AbortToken)).Should().Be(0);
    }

    /// <summary>Renew and release fan out over every child slot.</summary>
    public virtual async Task should_renew_and_release_composite_slot_lease()
    {
        var provider = GetSemaphoreProvider();
        var (first, second) = CompositeTestResources.CreatePair();
        var handle = await provider.AcquireAllAsync(
            [new DistributedSemaphoreRequest(first, 5), new DistributedSemaphoreRequest(second, 2)],
            cancellationToken: AbortToken
        );

        try
        {
            var renewed = await handle.RenewAsync(TimeSpan.FromSeconds(30), AbortToken);

            renewed.Should().BeTrue();
            (await provider.GetHolderCountAsync(first, AbortToken)).Should().Be(1);
            (await provider.GetHolderCountAsync(second, AbortToken)).Should().Be(1);

            await handle.ReleaseAsync();

            (await provider.GetHolderCountAsync(first, AbortToken)).Should().Be(0);
            (await provider.GetHolderCountAsync(second, AbortToken)).Should().Be(0);
        }
        finally
        {
            await handle.DisposeAsync();
        }
    }

    /// <summary>
    /// A canonical set naming one resource is not a composite: the semaphore's own slot lease is returned, keeping
    /// its bare resource name and its real fencing token (a composite's is <see langword="null"/>).
    /// </summary>
    public virtual async Task should_return_child_lease_for_single_canonical_semaphore_resource()
    {
        var provider = GetSemaphoreProvider();
        var (first, _) = CompositeTestResources.CreatePair();

        var handle = await provider.AcquireAllAsync(
            [new DistributedSemaphoreRequest(first, 5), new DistributedSemaphoreRequest(first, 5)],
            cancellationToken: AbortToken
        );

        try
        {
            handle.Resource.Should().Be(first);
            handle.FencingToken.Should().NotBeNull();
            (await provider.GetHolderCountAsync(first, AbortToken)).Should().Be(1);
        }
        finally
        {
            await handle.DisposeAsync();
        }

        (await provider.GetHolderCountAsync(first, AbortToken)).Should().Be(0);
    }

    /// <summary>
    /// Two callers name the same two capacity-1 semaphores in OPPOSITE order and race. Canonicalization sorts both
    /// sets to the same ordinal order, so one caller takes both slots and the other follows. Every iteration must see
    /// both composites succeed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Capacity 1 is what gives this teeth: a capacity-1 semaphore is mutually exclusive, so without the canonical
    /// sort caller X holds a slot of <c>first</c> while waiting on <c>second</c> and caller Y holds a slot of
    /// <c>second</c> while waiting on <c>first</c> — a circular wait that never resolves.
    /// </para>
    /// <para>
    /// Both callers are released from one barrier onto separate pool threads. Without that they do not genuinely
    /// interleave: an in-process acquire can complete without ever yielding, so caller X would run to completion
    /// before caller Y was constructed, and a serialized pair cannot form a circular wait — the test would pass even
    /// with the ordinal sort deleted. Probabilistic by nature, hence the repeats; the deterministic ordering guard
    /// lives in the unit tests.
    /// </para>
    /// </remarks>
    public virtual async Task should_not_deadlock_when_two_callers_request_opposite_semaphore_orders_concurrently()
    {
        var provider = GetSemaphoreProvider();

        for (var iteration = 0; iteration < 20; iteration++)
        {
            var (first, second) = CompositeTestResources.CreatePair();
            var options = new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.FromSeconds(5) };

            using var startLine = new SemaphoreSlim(0, 2);
            using var callerSource = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
            var callerToken = callerSource.Token;

            var callerX = Task.Run(
                async () =>
                {
                    await startLine.WaitAsync(callerToken);

                    return await _AcquireThenReleaseAsync(
                        provider,
                        [new DistributedSemaphoreRequest(first, 1), new DistributedSemaphoreRequest(second, 1)],
                        options,
                        callerToken
                    );
                },
                callerToken
            );

            var callerY = Task.Run(
                async () =>
                {
                    await startLine.WaitAsync(callerToken);

                    return await _AcquireThenReleaseAsync(
                        provider,
                        [new DistributedSemaphoreRequest(second, 1), new DistributedSemaphoreRequest(first, 1)],
                        options,
                        callerToken
                    );
                },
                callerToken
            );

            startLine.Release(2);

            var race = Task.WhenAll(callerX, callerY);
            bool acquiredX;
            bool acquiredY;

            try
            {
                // The blocked caller re-probes on the provider's TimeProvider-driven backoff, so advance the clock
                // until both settle. The bounded WaitAsync means a genuine deadlock FAILS rather than hangs.
                for (var i = 0; i < 60 && !race.IsCompleted; i++)
                {
                    await AdvanceTimeAsync(TimeSpan.FromMilliseconds(200), AbortToken);
                    await Task.Yield();
                }

                await race.WaitAsync(TimeSpan.FromSeconds(30), AbortToken);

                acquiredX = await callerX;
                acquiredY = await callerY;
            }
            finally
            {
                // On an ordering regression the callers deadlock and the WaitAsync above throws while they are still
                // running against a real backend. Cancel and drain them, or an abandoned caller would hold real slots
                // past the end of this test and turn one honest failure into a cascade of unrelated ones.
                await callerSource.CancelAsync();
                await _DrainCancelledRaceAsync(race);
            }

            acquiredX.Should().BeTrue($"caller X must form its composite on iteration {iteration}");
            acquiredY.Should().BeTrue($"caller Y must form its composite on iteration {iteration}");
        }
    }

    private static async Task<bool> _AcquireThenReleaseAsync(
        IDistributedSemaphoreProvider provider,
        IReadOnlyList<DistributedSemaphoreRequest> requests,
        DistributedLockAcquireOptions options,
        CancellationToken cancellationToken
    )
    {
        var handle = await provider.TryAcquireAllAsync(requests, options, cancellationToken);

        if (handle is null)
        {
            return false;
        }

        await handle.DisposeAsync();

        return true;
    }

    /// <summary>
    /// Awaits the cancelled race so both callers are settled before the test leaves the iteration, swallowing the
    /// cancellation and any acquire fault they report on the way down. Only reached when the race has already failed,
    /// so there is no outcome left to assert — the point is to leave no caller running.
    /// </summary>
#pragma warning disable CA1031, RCS1075 // The race has already failed; draining it must not mask that failure with a new one.
    private static async Task _DrainCancelledRaceAsync(Task race)
    {
        try
        {
            await race;
        }
        catch (Exception) { }
    }
#pragma warning restore CA1031, RCS1075
}
