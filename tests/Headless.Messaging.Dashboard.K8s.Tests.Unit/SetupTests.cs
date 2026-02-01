// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Dashboard;
using Headless.Messaging.Dashboard.GatewayProxy;
using Headless.Messaging.Dashboard.GatewayProxy.Requester;
using Headless.Messaging.Dashboard.K8s;
using Headless.Messaging.Dashboard.NodeDiscovery;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class SetupTests : TestBase
{
    [Fact]
    public void AddMessagingDashboardStandalone_should_register_required_services()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        services.AddMessagingDashboardStandalone();

        // then
        var provider = services.BuildServiceProvider();

        provider.GetService<K8sDiscoveryOptions>().Should().NotBeNull();
        provider.GetService<DashboardOptions>().Should().NotBeNull();
        provider.GetService<INodeDiscoveryProvider>().Should().NotBeNull();
        provider.GetService<INodeDiscoveryProvider>().Should().BeOfType<K8sNodeDiscoveryProvider>();
    }

    [Fact]
    public void AddMessagingDashboardStandalone_should_register_http_services()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        services.AddMessagingDashboardStandalone();

        // then
        var provider = services.BuildServiceProvider();

        provider.GetService<IHttpRequester>().Should().NotBeNull();
        provider.GetService<IHttpClientCache>().Should().NotBeNull();
        provider.GetService<IRequestMapper>().Should().NotBeNull();
        // Note: GatewayProxyAgent requires ConsulDiscoveryOptions which is not registered with K8s-only setup
        // So we just verify the other services are registered
        services.Should().Contain(sd => sd.ServiceType == typeof(GatewayProxyAgent));
    }

    [Fact]
    public void AddMessagingDashboardStandalone_should_apply_dashboard_options()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        services.AddMessagingDashboardStandalone(
            option => option.PathMatch = "/custom-path",
            k8SOption => k8SOption.ShowOnlyExplicitVisibleNodes = false
        );

        // then
        var provider = services.BuildServiceProvider();
        var dashboardOptions = provider.GetRequiredService<DashboardOptions>();
        var k8SOptions = provider.GetRequiredService<K8sDiscoveryOptions>();

        dashboardOptions.PathMatch.Should().Be("/custom-path");
        k8SOptions.ShowOnlyExplicitVisibleNodes.Should().BeFalse();
    }

    [Fact]
    public void AddMessagingDashboardStandalone_should_work_with_null_options()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        services.AddMessagingDashboardStandalone(null, null);

        // then
        var provider = services.BuildServiceProvider();

        provider.GetService<K8sDiscoveryOptions>().Should().NotBeNull();
        provider.GetService<DashboardOptions>().Should().NotBeNull();
    }

    [Fact]
    public void AddMessagingDashboardStandalone_should_register_services_as_singletons()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMessagingDashboardStandalone();

        // then
        var provider = services.BuildServiceProvider();

        var options1 = provider.GetRequiredService<K8sDiscoveryOptions>();
        var options2 = provider.GetRequiredService<K8sDiscoveryOptions>();

        options1.Should().BeSameAs(options2);
    }
}
