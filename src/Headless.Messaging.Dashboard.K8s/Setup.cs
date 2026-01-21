// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Configuration;
using Headless.Messaging.Dashboard;
using Headless.Messaging.Dashboard.K8s;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Contains extension methods to <see cref="IServiceCollection" /> for configuring consistence services.</summary>
// ReSharper disable once InconsistentNaming
public static class MessagesK8sDashboardSetup
{
    /// <summary>
    /// Run only messaging dashboard to view data based on the nodes discovered in Kubernetes.
    /// </summary>
    /// <param name="services">The services available in the application.</param>
    /// <param name="option">An action to configure the <see cref="DashboardOptions" />.</param>
    /// <param name="k8SOption">An action to configure the <see cref="K8sDiscoveryOptions" />.</param>
    /// <returns>An <see cref="MessagingBuilder" /> for application services.</returns>
    public static IServiceCollection AddMessagingDashboardStandalone(
        this IServiceCollection services,
        Action<DashboardOptions>? option = null,
        Action<K8sDiscoveryOptions>? k8SOption = null
    )
    {
        new DashboardOptionsExtension(option ?? (_ => { })).AddServices(services);
        new K8sDiscoveryOptionsExtension(k8SOption).AddServices(services);
        return services;
    }
}
