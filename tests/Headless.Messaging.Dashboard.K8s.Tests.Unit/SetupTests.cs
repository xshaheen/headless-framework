// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Dashboard;
using Headless.Messaging.Dashboard.GatewayProxy;
using Headless.Messaging.Dashboard.K8s;
using Headless.Messaging.Dashboard.NodeDiscovery;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class SetupTests : TestBase
{
    [Fact]
    public void should_register_required_services_when_add_messaging_dashboard_standalone()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        services.AddMessagingDashboardStandalone(dashboard => dashboard.WithNoAuth());

        // then
        var provider = services.BuildServiceProvider();

        provider.GetService<K8sDiscoveryOptions>().Should().NotBeNull();
        provider.GetService<MessagingDashboardOptionsBuilder>().Should().NotBeNull();
        provider.GetService<INodeDiscoveryProvider>().Should().NotBeNull();
        provider.GetService<INodeDiscoveryProvider>().Should().BeOfType<K8sNodeDiscoveryProvider>();
    }

    [Fact]
    public void should_register_http_services_when_add_messaging_dashboard_standalone()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        services.AddMessagingDashboardStandalone(dashboard => dashboard.WithNoAuth());

        // then
        var provider = services.BuildServiceProvider();

        provider.GetService<IHttpClientFactory>().Should().NotBeNull();
        provider.GetService<IRequestMapper>().Should().NotBeNull();
        // Note: GatewayProxyAgent requires ConsulDiscoveryOptions which is not registered with K8s-only setup
        // So we just verify the other services are registered
        services.Should().Contain(sd => sd.ServiceType == typeof(GatewayProxyAgent));
    }

    [Fact]
    public void should_apply_dashboard_options_when_add_messaging_dashboard_standalone()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        services.AddMessagingDashboardStandalone(
            option => option.SetBasePath("/custom-path").WithNoAuth(),
            configureK8s => configureK8s.ShowOnlyExplicitVisibleNodes = false
        );

        // then
        var provider = services.BuildServiceProvider();
        var builder = provider.GetRequiredService<MessagingDashboardOptionsBuilder>();
        var k8sOptions = provider.GetRequiredService<K8sDiscoveryOptions>();

        builder.BasePath.Should().Be("/custom-path");
        k8sOptions.ShowOnlyExplicitVisibleNodes.Should().BeFalse();
    }

    [Fact]
    public void should_work_with_null_k8s_options_when_add_messaging_dashboard_standalone()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when — a null configureK8s uses K8s defaults; the dashboard builder still must pick an auth
        // mode (WithNoAuth here) because dashboard auth is mandatory (see MessagingDashboardOptionsBuilder.Validate).
        services.AddMessagingDashboardStandalone(dashboard => dashboard.WithNoAuth(), null);

        // then
        var provider = services.BuildServiceProvider();

        provider.GetService<K8sDiscoveryOptions>().Should().NotBeNull();
        provider.GetService<MessagingDashboardOptionsBuilder>().Should().NotBeNull();
    }

    [Fact]
    public void should_register_services_as_singletons_when_add_messaging_dashboard_standalone()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMessagingDashboardStandalone(dashboard => dashboard.WithNoAuth());

        // then
        var provider = services.BuildServiceProvider();

        var options1 = provider.GetRequiredService<K8sDiscoveryOptions>();
        var options2 = provider.GetRequiredService<K8sDiscoveryOptions>();

        options1.Should().BeSameAs(options2);
    }
}
