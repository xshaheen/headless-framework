// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.DistributedLocks;
using Headless.Redis;
using Headless.Redis.Testing;
using Microsoft.Extensions.Logging;

namespace Tests;

[Collection<RedisTestFixture>]
public sealed class RedisDistributedSemaphoreProviderTests(RedisTestFixture fixture)
    : DistributedSemaphoreProviderTestsBase
{
    // Deliberately no FlushAllAsync here. The Redis collection runs its classes in parallel
    // (DisableParallelization = false), so a flush in InitializeAsync wipes a sibling test's keys mid-run.
    // Every scenario in the base scopes its resources under a per-test GUID prefix, so isolation is already
    // guaranteed by naming and a flush would only add cross-class interference.

    protected override IDistributedSemaphoreProvider GetSemaphoreProvider(DistributedLockOptions? options = null)
    {
        return new DistributedSemaphoreProvider(
            fixture.SemaphoreStorage,
            outboxBus: null,
            options ?? new DistributedLockOptions(),
            new SequentialGuidGenerator(SequentialGuidType.SqlServer),
            TimeProvider.System,
            LoggerFactory.CreateLogger<DistributedSemaphoreProvider>()
        );
    }

    protected override TimeProvider TimeProvider => TimeProvider.System;

    protected override async Task AdvanceTimeAsync(TimeSpan amount, CancellationToken cancellationToken)
    {
        await Task.Delay(amount + TimeSpan.FromMilliseconds(50), TimeProvider.System, cancellationToken);
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
}
