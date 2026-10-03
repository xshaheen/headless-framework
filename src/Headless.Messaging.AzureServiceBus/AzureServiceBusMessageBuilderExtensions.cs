// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.AzureServiceBus;
using Headless.Messaging.Registration;

namespace Headless.Messaging;

/// <summary>Extension methods that attach Azure Service Bus provider-specific options to a message contract.</summary>
[PublicAPI]
public static class AzureServiceBusMessageBuilderExtensions
{
    /// <summary>
    /// Configures Azure Service Bus options for <typeparamref name="TMessage"/> publish operations.
    /// </summary>
    /// <typeparam name="TMessage">The message type being registered.</typeparam>
    /// <param name="builder">The Bus route of the message contract.</param>
    /// <param name="configure">A delegate that configures the Azure Service Bus options.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static IBusContractBuilder<TMessage> UseAzureServiceBus<TMessage>(
        this IBusContractBuilder<TMessage> builder,
        Action<AzureServiceBusMessageConfigBuilder<TMessage>> configure
    )
        where TMessage : class
    {
        Argument.IsNotNull(builder);
        Argument.IsNotNull(configure);

        var configBuilder = new AzureServiceBusMessageConfigBuilder<TMessage>();
        configure(configBuilder);
        ((IMessageProviderConfigBuilder<TMessage>)builder).SetMessageProviderConfig(configBuilder.Build());

        return builder;
    }

    /// <summary>Configures Azure Service Bus options for <typeparamref name="TMessage"/> queue publish operations.</summary>
    public static IQueueContractBuilder<TMessage> UseAzureServiceBus<TMessage>(
        this IQueueContractBuilder<TMessage> builder,
        Action<AzureServiceBusMessageConfigBuilder<TMessage>> configure
    )
        where TMessage : class
    {
        Argument.IsNotNull(builder);
        Argument.IsNotNull(configure);

        var configBuilder = new AzureServiceBusMessageConfigBuilder<TMessage>();
        configure(configBuilder);
        ((IMessageProviderConfigBuilder<TMessage>)builder).SetMessageProviderConfig(configBuilder.Build());

        return builder;
    }
}
