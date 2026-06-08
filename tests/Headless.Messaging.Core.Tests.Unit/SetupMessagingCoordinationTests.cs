// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class SetupMessagingCoordinationTests
{
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
        var membership = TestNodeMembership.Active("node-a", 7);
        services.AddSingleton<INodeMembership>(membership);

        // when
        services.AddHeadlessMessaging(_ => { });
        using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredService<INodeMembership>().Should().BeSameAs(membership);
    }
}
