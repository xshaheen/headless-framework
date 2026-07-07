// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Configuration;
using Headless.Messaging.InMemoryStorage;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// InMemory binding of the cross-provider <see cref="DeadOwnerReclaimConformanceTests"/>. InMemory state is
/// per-storage-instance, so a second concurrent bridge host must share the same instance to act on the same rows.
/// </summary>
public sealed class InMemoryDeadOwnerReclaimConformanceTests : DeadOwnerReclaimConformanceTests
{
    protected override void ConfigureStorage(MessagingSetupBuilder setup) => setup.UseInMemoryStorage();

    protected override bool SharesStorageInstanceForConcurrency => true;

    [Fact]
    public override Task should_reclaim_dead_owner_published_and_received_rows() =>
        base.should_reclaim_dead_owner_published_and_received_rows();

    [Fact]
    public override Task should_not_reclaim_suspected_owner_rows() => base.should_not_reclaim_suspected_owner_rows();

    [Fact]
    public override Task should_reclaim_once_when_surfaced_by_both_event_and_reconcile() =>
        base.should_reclaim_once_when_surfaced_by_both_event_and_reconcile();

    [Fact]
    public override Task should_fence_a_restarted_incarnation() => base.should_fence_a_restarted_incarnation();

    [Fact]
    public override Task should_recover_aged_out_owner_via_lease_floor() =>
        base.should_recover_aged_out_owner_via_lease_floor();

    [Fact]
    public override Task should_reclaim_once_under_two_concurrent_bridges() =>
        base.should_reclaim_once_under_two_concurrent_bridges();
}
