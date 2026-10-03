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
