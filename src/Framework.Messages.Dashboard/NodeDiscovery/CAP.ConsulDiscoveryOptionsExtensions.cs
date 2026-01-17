// Copyright (c) Mahmoud Shaheen. All rights reserved.

using DotNetCore.CAP.Dashboard.GatewayProxy;
using DotNetCore.CAP.Dashboard.NodeDiscovery;
using Framework.Checks;
using Framework.Messages.Configuration;
using Framework.Messages.Dashboard.GatewayProxy.Requester;
using Framework.Messages.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace DotNetCore.CAP.Dashboard.NodeDiscovery
{
    internal sealed class ConsulDiscoveryOptionsExtension(Action<ConsulDiscoveryOptions> option)
        : IMessagesOptionsExtension
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
}

namespace Microsoft.Extensions.DependencyInjection
{
    public static class CapDiscoveryOptionsExtensions
    {
        /// <summary>
        /// Default use kubernetes as service discovery.
        /// </summary>
        public static CapOptions UseConsulDiscovery(this CapOptions capOptions)
        {
            return capOptions.UseConsulDiscovery(_ => { });
        }

        public static CapOptions UseConsulDiscovery(this CapOptions capOptions, Action<ConsulDiscoveryOptions> options)
        {
            Argument.IsNotNull(options);

            capOptions.RegisterExtension(new ConsulDiscoveryOptionsExtension(options));

            return capOptions;
        }
    }
}
