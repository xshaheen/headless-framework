// Copyright (c) Mahmoud Shaheen. All rights reserved.

using DotNetCore.CAP.Dashboard.GatewayProxy;
using DotNetCore.CAP.Dashboard.K8s;
using DotNetCore.CAP.Dashboard.NodeDiscovery;
using Framework.Checks;
using Framework.Messages.Configuration;
using Framework.Messages.Dashboard.GatewayProxy.Requester;
using Microsoft.Extensions.DependencyInjection;

namespace DotNetCore.CAP.Dashboard.K8s
{
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
}

namespace Microsoft.Extensions.DependencyInjection
{
    public static class CapDiscoveryOptionsExtensions
    {
        // ReSharper disable once InconsistentNaming
        /// <summary>
        /// Use K8s as a service discovery to view data from other nodes in the Dashboard.
        /// </summary>
        /// <param name="capOptions"></param>
        public static CapOptions UseK8sDiscovery(this CapOptions capOptions)
        {
            return capOptions.UseK8sDiscovery(opt => { });
        }

        // ReSharper disable once InconsistentNaming
        /// <summary>
        /// Use K8s as a service discovery to view data from other nodes in the Dashboard.
        /// </summary>
        /// <param name="capOptions"></param>
        /// <param name="options">The option of <see cref="K8sDiscoveryOptions" /></param>
        /// <exception cref="ArgumentNullException"></exception>
        public static CapOptions UseK8sDiscovery(this CapOptions capOptions, Action<K8sDiscoveryOptions> options)
        {
            Argument.IsNotNull(options);

            capOptions.RegisterExtension(new K8sDiscoveryOptionsExtension(options));

            return capOptions;
        }
    }
}
