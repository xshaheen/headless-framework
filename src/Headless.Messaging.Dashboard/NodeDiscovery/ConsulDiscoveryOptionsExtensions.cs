// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Headless.Messaging.Configuration;
using Headless.Messaging.Dashboard.GatewayProxy;
using Headless.Messaging.Dashboard.GatewayProxy.Requester;
using Headless.Messaging.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Messaging.Dashboard.NodeDiscovery;

internal sealed class ConsulDiscoveryOptionsExtension(Action<ConsulDiscoveryOptions> option) : IMessagesOptionsExtension
{
    public void AddServices(IServiceCollection services)
    {
        var discoveryOptions = new ConsulDiscoveryOptions();

        option?.Invoke(discoveryOptions);
        services.AddSingleton(discoveryOptions);

        services.AddSingleton<IHttpRequester, HttpClientHttpRequester>();
        services.AddSingleton<IHttpClientCache, MemoryHttpClientCache>();
        services.AddSingleton<IRequestMapper, RequestMapper>();
        services.AddSingleton<GatewayProxyAgent>();
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
