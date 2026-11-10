// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard;
using Headless.Messaging.Dashboard;
using Headless.Messaging.Dashboard.K8s;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class SetupTests : TestBase
{
    private static readonly string _Key = new('k', 32);

    [Fact]
    public void should_register_k8s_discovery_when_add_messaging_standalone()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        services.AddHeadlessDashboard(dashboard =>
            dashboard
                .WithHostAuthentication()
                .AddMessagingStandalone(
                    options => options.Forwarding.Key = _Key,
                    k8s =>
                    {
                        k8s.Scheme = "https";
                        k8s.ShowOnlyExplicitVisibleNodes = false;
                    }
                )
        );

        // then
        using var provider = services.BuildServiceProvider();
        var k8sOptions = provider.GetRequiredService<K8sDiscoveryOptions>();

        k8sOptions.Scheme.Should().Be("https");
        k8sOptions.ShowOnlyExplicitVisibleNodes.Should().BeFalse();
        provider.GetService<INodeDiscoveryProvider>().Should().BeOfType<K8sNodeDiscoveryProvider>();
    }

    [Fact]
    public void should_bind_module_options_without_headless_messaging_when_add_messaging_standalone()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        services.AddHeadlessDashboard(dashboard =>
            dashboard
                .WithHostAuthentication()
                .AddMessagingStandalone(
                    options =>
                    {
                        options.NodeName = "dashboard";
                        options.Forwarding.Key = _Key;
                    },
                    _ => { }
                )
        );

        // then
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<MessagingDashboardOptions>>().Value;

        options.NodeName.Should().Be("dashboard");
        options.Forwarding.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void should_throw_when_k8s_action_is_null()
    {
        // given
        var services = new ServiceCollection();

        // when
        var act = () =>
            services.AddHeadlessDashboard(dashboard =>
                dashboard
                    .WithHostAuthentication()
                    .AddMessagingStandalone(options => options.Forwarding.Key = _Key, null!)
            );

        // then
        act.Should().Throw<ArgumentNullException>();
    }
}
