// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.DistributedLocks;
using Headless.DistributedLocks.InMemory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class InMemoryDistributedSemaphoreProviderTests : DistributedSemaphoreProviderTestsBase
{
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly IGuidGenerator _guidGenerator = new SequentialGuidGenerator(SequentialGuidType.Version7);

    protected override IDistributedSemaphoreProvider GetSemaphoreProvider(DistributedLockOptions? options = null)
    {
        return new DistributedSemaphoreProvider(
            new InMemoryDistributedSemaphoreStorage(_timeProvider),
            outboxBus: null,
            options ?? new DistributedLockOptions(),
            _guidGenerator,
            _timeProvider,
            LoggerFactory.CreateLogger<DistributedSemaphoreProvider>()
        );
    }

    protected override TimeProvider TimeProvider => _timeProvider;

    /// <summary>
    /// Advances the fake clock in small steps, draining the thread pool between each so any waiter that re-probes on
    /// the provider's TimeProvider-driven backoff observes the advance before the next step.
    /// </summary>
    protected override async Task AdvanceTimeAsync(TimeSpan amount, CancellationToken cancellationToken)
    {
        var step = TimeSpan.FromMilliseconds(100);
        var remaining = amount;

        while (remaining > TimeSpan.Zero)
        {
            var currentStep = remaining < step ? remaining : step;
            _timeProvider.Advance(currentStep);
            remaining -= currentStep;

            for (var i = 0; i < 200; i++)
            {
                if (i % 100 is 0)
                {
                    await TimeProvider.System.Delay(TimeSpan.FromMilliseconds(1), cancellationToken);
                }
                else
                {
                    await Task.Yield();
                }
            }
        }
    }

    [Fact]
    public override Task should_acquire_composite_slots_across_differently_sized_semaphores() =>
        base.should_acquire_composite_slots_across_differently_sized_semaphores();

    [Fact]
    public override Task should_reject_conflicting_max_count_for_one_resource() =>
        base.should_reject_conflicting_max_count_for_one_resource();

    [Fact]
    public override Task should_acquire_composite_slots_in_canonical_order_and_deduplicate() =>
        base.should_acquire_composite_slots_in_canonical_order_and_deduplicate();

    [Fact]
    public override Task should_release_earlier_slots_when_later_semaphore_is_saturated() =>
        base.should_release_earlier_slots_when_later_semaphore_is_saturated();

    [Fact]
    public override Task should_renew_and_release_composite_slot_lease() =>
        base.should_renew_and_release_composite_slot_lease();

    [Fact]
    public override Task should_return_child_lease_for_single_canonical_semaphore_resource() =>
        base.should_return_child_lease_for_single_canonical_semaphore_resource();

    [Fact]
    public override Task should_not_deadlock_when_two_callers_request_opposite_semaphore_orders_concurrently() =>
        base.should_not_deadlock_when_two_callers_request_opposite_semaphore_orders_concurrently();
}
