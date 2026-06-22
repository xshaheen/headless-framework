// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Configuration;
using Headless.Messaging.Dashboard.GatewayProxy;
using Headless.Messaging.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Messaging.Dashboard.NodeDiscovery;

internal sealed class ConsulDiscoveryOptionsExtension(Action<ConsulDiscoveryOptions> option) : IMessagesOptionsExtension
{
    public void AddServices(IServiceCollection services)
    {
        var discoveryOptions = new ConsulDiscoveryOptions();

        option?.Invoke(discoveryOptions);
        services.AddSingleton(discoveryOptions);

        services.AddHttpClient();
        services.TryAddSingleton<IRequestMapper, RequestMapper>();
        services.TryAddSingleton<GatewayProxyAgent>();
        services.AddSingleton<IProcessingServer, ConsulProcessingNodeServer>();
        services.AddSingleton<INodeDiscoveryProvider, ConsulNodeDiscoveryProvider>();
    }
}

public static class MessagingDiscoveryOptionsExtensions
{
    /// <summary>
    /// Enables Consul-based node discovery for the Messaging Dashboard using default options.
    /// Peer nodes tagged <c>messaging</c> in Consul will appear in the dashboard node list.
    /// </summary>
    /// <returns>The builder for chaining.</returns>
    public static MessagingSetupBuilder UseConsulDiscovery(this MessagingSetupBuilder setup)
    {
        return setup.UseConsulDiscovery(_ => { });
    }

    /// <summary>
    /// Enables Consul-based node discovery for the Messaging Dashboard with custom options.
    /// </summary>
    /// <param name="setup">The messaging setup builder.</param>
    /// <param name="options">An action to configure <see cref="ConsulDiscoveryOptions"/>.</param>
    /// <returns>The builder for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is <see langword="null"/>.</exception>
    public static MessagingSetupBuilder UseConsulDiscovery(
        this MessagingSetupBuilder setup,
        Action<ConsulDiscoveryOptions> options
    )
    {
        Argument.IsNotNull(options);

        setup.RegisterExtension(new ConsulDiscoveryOptionsExtension(options));

        return setup;
    }
}
