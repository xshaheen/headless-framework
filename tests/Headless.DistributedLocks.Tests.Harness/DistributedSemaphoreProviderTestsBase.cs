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

    /// <summary>
    /// Two ordinal-ordered resources under a per-test unique prefix, so the composite's canonical order is known
    /// (<c>:a</c> sorts before <c>:b</c>) and no other test can contend with this one.
    /// </summary>
    protected static (string First, string Second) CreateCompositeResources()
    {
        var prefix = $"composite:{Guid.NewGuid():N}";

        return ($"{prefix}:a", $"{prefix}:b");
    }

    /// <summary>Covers SC2: one slot of each named semaphore, whatever their individual capacities.</summary>
    public virtual async Task should_acquire_composite_slots_across_differently_sized_semaphores()
    {
        var provider = GetSemaphoreProvider();
        var (first, second) = CreateCompositeResources();

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
        var (first, _) = CreateCompositeResources();

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
        var (first, second) = CreateCompositeResources();

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
        var (first, second) = CreateCompositeResources();
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
        var (first, second) = CreateCompositeResources();
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
        var (first, _) = CreateCompositeResources();

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
}
