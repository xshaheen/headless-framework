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
    /// Default use kubernetes as service discovery.
    /// </summary>
    public static MessagingOptions UseConsulDiscovery(this MessagingOptions messagingOptions)
    {
        return messagingOptions.UseConsulDiscovery(_ => { });
    }

    public static MessagingOptions UseConsulDiscovery(
        this MessagingOptions messagingOptions,
        Action<ConsulDiscoveryOptions> options
    )
    {
        Argument.IsNotNull(options);

        messagingOptions.RegisterExtension(new ConsulDiscoveryOptionsExtension(options));

        return messagingOptions;
    }
}
