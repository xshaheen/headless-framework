// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Framework.Messages.Configuration;
using Framework.Messages.GatewayProxy;
using Framework.Messages.GatewayProxy.Requester;
using Framework.Messages.NodeDiscovery;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Messages;

// ReSharper disable once InconsistentNaming
internal sealed class K8sDiscoveryOptionsExtension(Action<K8sDiscoveryOptions>? option) : IMessagesOptionsExtension
{
    public void AddServices(IServiceCollection services)
    {
        var k8SOptions = new K8sDiscoveryOptions();

        option?.Invoke(k8SOptions);
        services.AddSingleton(k8SOptions);

        services.AddSingleton<IHttpRequester, HttpClientHttpRequester>();
        services.AddSingleton<IHttpClientCache, MemoryHttpClientCache>();
        services.AddSingleton<IRequestMapper, RequestMapper>();
        services.AddSingleton<GatewayProxyAgent>();
        services.AddSingleton<INodeDiscoveryProvider, K8sNodeDiscoveryProvider>();
    }
}

public static class MessagingDiscoveryOptionsExtensions
{
    // ReSharper disable once InconsistentNaming
    /// <summary>
    /// Use K8s as a service discovery to view data from other nodes in the Dashboard.
    /// </summary>
    /// <param name="messagingOptions"></param>
    public static MessagingOptions UseK8sDiscovery(this MessagingOptions messagingOptions)
    {
        return messagingOptions.UseK8sDiscovery(opt => { });
    }

    // ReSharper disable once InconsistentNaming
    /// <summary>
    /// Use K8s as a service discovery to view data from other nodes in the Dashboard.
    /// </summary>
    /// <param name="messagingOptions"></param>
    /// <param name="options">The option of <see cref="K8sDiscoveryOptions" /></param>
    /// <exception cref="ArgumentNullException"></exception>
    public static MessagingOptions UseK8sDiscovery(
        this MessagingOptions messagingOptions,
        Action<K8sDiscoveryOptions> options
    )
    {
        Argument.IsNotNull(options);

        messagingOptions.RegisterExtension(new K8sDiscoveryOptionsExtension(options));

        return messagingOptions;
    }
}
