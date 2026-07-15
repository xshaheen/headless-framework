// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.DistributedLocks;
using Headless.Redis.Testing;
using Microsoft.Extensions.Logging;

namespace Tests;

[Collection<RedisTestFixture>]
public sealed class RedisDistributedLockConformanceTests(RedisTestFixture fixture) : DistributedLockTestsBase
{
    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await fixture.ConnectionMultiplexer.FlushAllAsync();
    }

    protected override IDistributedLock GetLockProvider()
    {
        return new DistributedLock(
            fixture.LockStorage,
            outboxBus: null,
            new DistributedLockOptions(),
            new SequentialGuidGenerator(SequentialGuidType.SqlServer),
            TimeProvider.System,
            LoggerFactory.CreateLogger<DistributedLock>()
        );
    }

    [Fact]
    public override Task should_acquire_composite_in_canonical_order_and_deduplicate()
    {
        return base.should_acquire_composite_in_canonical_order_and_deduplicate();
    }

    [Fact]
    public override Task should_acquire_opposite_composite_orders_sequentially()
    {
        return base.should_acquire_opposite_composite_orders_sequentially();
    }

    [Fact]
    public override Task should_release_earlier_composite_children_when_later_resource_is_contended()
    {
        return base.should_release_earlier_composite_children_when_later_resource_is_contended();
    }

    [Fact]
    public override Task should_renew_and_release_composite_lease()
    {
        return base.should_renew_and_release_composite_lease();
    }

    [Fact]
    public override Task should_dispatch_composite_renew_and_release_through_provider()
    {
        return base.should_dispatch_composite_renew_and_release_through_provider();
    }

    [Fact]
    public override Task should_keep_composite_resources_when_disposed_without_release()
    {
        return base.should_keep_composite_resources_when_disposed_without_release();
    }
}
