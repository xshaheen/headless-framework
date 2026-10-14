// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Messaging;
using Headless.Messaging.Dashboard;
using Headless.Messaging.Dashboard.Consul;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class ConsulDiscoveryOptionsExtensionsTests : TestBase
{
    [Fact]
    public void should_preserve_consul_discovery_name_without_contributing_provider_capabilities()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(setup => setup.UseConsulDiscovery());

        using var provider = services.BuildServiceProvider();

        typeof(MessagingConsulDiscoveryOptionsExtensions)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Should()
            .Contain(method => method.Name == "UseConsulDiscovery");
        provider
            .GetRequiredService<IMessagingCapabilityModel>()
            .DeclaredCapabilities.Should()
            .BeEmpty("node discovery is cross-cutting and is not a transport, storage, or coordination provider");
    }

    [Fact]
    public void should_register_current_node_identity_with_configured_node_name()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(setup => setup.UseConsulDiscovery(options => options.NodeName = "node-a"));

        // when
        using var provider = services.BuildServiceProvider();

        // then - the dashboard gateway reads this to never proxy a request back to the current node
        provider.GetRequiredService<CurrentNodeIdentity>().Name.Should().Be("node-a");
    }
}
