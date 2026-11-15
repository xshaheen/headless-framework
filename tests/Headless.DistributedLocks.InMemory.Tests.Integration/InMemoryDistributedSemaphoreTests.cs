// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.DistributedLocks.InMemory;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class InMemoryDistributedSemaphoreTests : DistributedSemaphoreStorageTestsBase
{
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly InMemoryDistributedSemaphoreStorage _storage;

    public InMemoryDistributedSemaphoreTests()
    {
        _storage = new(_timeProvider);
    }

    protected override IDistributedSemaphoreStorage SemaphoreStorage => _storage;

    protected override TimeProvider TimeProvider => _timeProvider;

    protected override Task AdvanceTimeAsync(TimeSpan amount, CancellationToken cancellationToken)
    {
        _timeProvider.Advance(amount);
        return Task.CompletedTask;
    }

    [Fact]
    public override Task should_allow_up_to_max_count_holders()
    {
        return base.should_allow_up_to_max_count_holders();
    }

    [Fact]
    public override Task should_not_exceed_max_count_under_concurrent_acquires()
    {
        return base.should_not_exceed_max_count_under_concurrent_acquires();
    }

    [Fact]
    public override Task should_reacquire_after_release_and_advance_fencing_token()
    {
        return base.should_reacquire_after_release_and_advance_fencing_token();
    }

    [Fact]
    public override Task should_not_advance_fencing_token_on_capacity_rejected_acquire()
    {
        return base.should_not_advance_fencing_token_on_capacity_rejected_acquire();
    }

    [Fact]
    public override Task should_reacquire_after_slot_expiry()
    {
        return base.should_reacquire_after_slot_expiry();
    }

    [Fact]
    public override Task should_fail_to_extend_an_expired_slot_that_no_acquire_has_pruned_yet()
    {
        return base.should_fail_to_extend_an_expired_slot_that_no_acquire_has_pruned_yet();
    }

    [Fact]
    public override Task should_extend_live_slot_and_not_re_add_expired_slot()
    {
        return base.should_extend_live_slot_and_not_re_add_expired_slot();
    }

    [Fact]
    public override Task should_not_shorten_live_slot_on_shorter_extend()
    {
        return base.should_not_shorten_live_slot_on_shorter_extend();
    }

    [Fact]
    public override Task should_validate_live_holder()
    {
        return base.should_validate_live_holder();
    }

    [Fact]
    public override Task should_exclude_expired_holder_from_count_and_validate()
    {
        return base.should_exclude_expired_holder_from_count_and_validate();
    }

    [Fact]
    public override Task should_allow_exactly_max_count_concurrent_holders_under_parallel_load()
    {
        return base.should_allow_exactly_max_count_concurrent_holders_under_parallel_load();
    }

    [Fact]
    public override Task should_issue_greater_fencing_token_after_all_slots_released()
    {
        return base.should_issue_greater_fencing_token_after_all_slots_released();
    }
}
