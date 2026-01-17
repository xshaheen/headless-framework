// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Framework.Messages;
using Framework.Messages.Configuration;
using Framework.Messages.Transport;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

public static class CapOptionsExtensions
{
    extension(CapOptions options)
    {
        /// <summary>
        /// Configuration to use pulsar in CAP.
        /// </summary>
        /// <param name="serverUrl">Pulsar bootstrap server urls.</param>
        public CapOptions UsePulsar(string serverUrl)
        {
            return options.UsePulsar(opt =>
            {
                opt.ServiceUrl = serverUrl;
            });
        }

        /// <summary>
        /// Configuration to use pulsar in CAP.
        /// </summary>
        /// <param name="configure">Provides programmatic configuration for the pulsar .</param>
        /// <returns></returns>
        public CapOptions UsePulsar(Action<PulsarOptions> configure)
        {
            Argument.IsNotNull(configure);

            options.RegisterExtension(new PulsarMessagesOptionsExtension(configure));

            return options;
        }
    }

    private sealed class PulsarMessagesOptionsExtension(Action<PulsarOptions> configure) : IMessagesOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton(new CapMessageQueueMakerService("Apache Pulsar"));

            services.Configure(configure);

            services.AddSingleton<ITransport, PulsarTransport>();
            services.AddSingleton<IConsumerClientFactory, PulsarConsumerClientFactory>();
            services.AddSingleton<IConnectionFactory, ConnectionFactory>();
        }
    }
}
