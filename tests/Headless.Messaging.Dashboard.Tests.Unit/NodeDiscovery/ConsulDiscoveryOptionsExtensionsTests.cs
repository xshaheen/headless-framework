// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Dashboard.NodeDiscovery;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.NodeDiscovery;

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
}
