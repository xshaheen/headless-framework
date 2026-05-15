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
        var k8SOptions = new K8sDiscoveryOptions();

        option?.Invoke(k8SOptions);
        services.AddSingleton(k8SOptions);

        services.AddHttpClient();
        services.TryAddSingleton<IRequestMapper, RequestMapper>();
        services.TryAddSingleton<GatewayProxyAgent>();
        services.AddSingleton<INodeDiscoveryProvider, K8sNodeDiscoveryProvider>();
    }
}

public static class MessagingDiscoveryOptionsExtensions
{
    // ReSharper disable once InconsistentNaming
    /// <summary>
    /// Use K8s as a service discovery to view data from other nodes in the Dashboard.
    /// </summary>
    /// <param name="setup"></param>
    public static MessagingSetupBuilder UseK8sDiscovery(this MessagingSetupBuilder setup)
    {
        return setup.UseK8sDiscovery(opt => { });
    }

    // ReSharper disable once InconsistentNaming
    /// <summary>
    /// Use K8s as a service discovery to view data from other nodes in the Dashboard.
    /// </summary>
    /// <param name="setup"></param>
    /// <param name="options">The option of <see cref="K8sDiscoveryOptions" /></param>
    /// <exception cref="ArgumentNullException"></exception>
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
