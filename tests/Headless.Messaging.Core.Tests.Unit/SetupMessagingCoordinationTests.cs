// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Messaging;
using Headless.Messaging.Coordination;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

public sealed class SetupMessagingCoordinationTests
{
    [Fact]
    public void should_register_dead_owner_recovery_bridge_unconditionally()
    {
        // given — no UseStorageLock, no real INodeMembership: recovery is always-on (KTD3)
        var services = new ServiceCollection();

        // when
        services.AddHeadlessMessaging(_ => { });

        // then — the reclaimer sink is registered
        services.Should().ContainSingle(d => d.ServiceType == typeof(MessagingDeadOwnerReclaimer));

        // and — the closed-generic hosted bridge over the messaging reclaimer is registered as IHostedService.
        // The bridge type is internal to Coordination.Core, so match it by the public IDeadOwnerRecoveryBridge
        // marker plus its closed reclaimer type-arg — both compile-time references, so a rename breaks the build.
        var bridge = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IHostedService)
            && d.ImplementationType is { IsGenericType: true } impl
            && typeof(IDeadOwnerRecoveryBridge).IsAssignableFrom(impl)
            && impl.GetGenericArguments()[0] == typeof(MessagingDeadOwnerReclaimer)
        );

        bridge
            .Should()
            .NotBeNull("the messaging dead-owner recovery bridge must be hosted regardless of UseStorageLock");
    }

    [Fact]
    public void should_register_null_node_membership_by_default()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessMessaging(_ => { });
        using var provider = services.BuildServiceProvider();

        // then
        var membership = provider.GetRequiredService<INodeMembership>();
        membership.Should().BeOfType<NullNodeMembership>();
        membership.Identity.Should().BeNull();
    }

    [Fact]
    public void should_not_shadow_registered_node_membership()
    {
        // given
        var services = new ServiceCollection();
        var membership = new ControlledNodeMembership();
        membership.SetIdentity("node-a", 7);
        services.AddSingleton<INodeMembership>(membership);

        // when
        services.AddHeadlessMessaging(_ => { });
        using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredService<INodeMembership>().Should().BeSameAs(membership);
    }
}
