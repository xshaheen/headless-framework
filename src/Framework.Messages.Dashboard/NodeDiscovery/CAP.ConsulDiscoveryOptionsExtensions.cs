// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Framework.Messages.Configuration;
using Framework.Messages.GatewayProxy;
using Framework.Messages.GatewayProxy.Requester;
using Framework.Messages.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Messages.NodeDiscovery;

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

public static class CapDiscoveryOptionsExtensions
{
    /// <summary>
    /// Default use kubernetes as service discovery.
    /// </summary>
    public static MessagingOptions UseConsulDiscovery(this MessagingOptions capOptions)
    {
        return capOptions.UseConsulDiscovery(_ => { });
    }

    public static MessagingOptions UseConsulDiscovery(
        this MessagingOptions capOptions,
        Action<ConsulDiscoveryOptions> options
    )
    {
        Argument.IsNotNull(options);

        capOptions.RegisterExtension(new ConsulDiscoveryOptionsExtension(options));

        return capOptions;
    }
}
