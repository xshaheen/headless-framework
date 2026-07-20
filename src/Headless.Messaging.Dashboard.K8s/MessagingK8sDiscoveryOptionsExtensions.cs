// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Configuration;
using Headless.Messaging.Dashboard.GatewayProxy;
using Headless.Messaging.Dashboard.NodeDiscovery;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Messaging.Dashboard.K8s;

// ReSharper disable once InconsistentNaming
internal sealed class K8sDiscoveryOptionsExtension(Action<K8sDiscoveryOptions>? option) : IMessagesOptionsExtension
{
    public void AddServices(IServiceCollection services)
    {
        var k8sOptions = new K8sDiscoveryOptions();

        option?.Invoke(k8sOptions);
        services.AddSingleton(k8sOptions);

        services.AddHttpClient();
        services.TryAddSingleton<IRequestMapper, RequestMapper>();
        services.TryAddSingleton<GatewayProxyAgent>();
        services.AddSingleton<INodeDiscoveryProvider, K8sNodeDiscoveryProvider>();
    }
}

public static class MessagingK8sDiscoveryOptionsExtensions
{
    // ReSharper disable once InconsistentNaming
    /// <summary>
    /// Enables Kubernetes-based node discovery for the Messaging Dashboard using default options.
    /// Services in the configured namespace that carry the <c>headless.messaging.visibility:show</c>
    /// label (or all services when <see cref="K8sDiscoveryOptions.ShowOnlyExplicitVisibleNodes"/>
    /// is <see langword="false"/>) will appear in the dashboard node list.
    /// </summary>
    /// <param name="setup">The messaging setup builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static MessagingSetupBuilder UseK8sDiscovery(this MessagingSetupBuilder setup)
    {
        return setup.UseK8sDiscovery(opt => { });
    }

    // ReSharper disable once InconsistentNaming
    /// <summary>
    /// Enables Kubernetes-based node discovery for the Messaging Dashboard with custom options.
    /// </summary>
    /// <param name="setup">The messaging setup builder.</param>
    /// <param name="options">An action to configure <see cref="K8sDiscoveryOptions"/>.</param>
    /// <returns>The builder for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is <see langword="null"/>.</exception>
    public static MessagingSetupBuilder UseK8sDiscovery(
        this MessagingSetupBuilder setup,
        Action<K8sDiscoveryOptions> options
    )
    {
        Argument.IsNotNull(options);

        setup.RegisterExtension(new K8sDiscoveryOptionsExtension(options));

        return setup;
    }
}
