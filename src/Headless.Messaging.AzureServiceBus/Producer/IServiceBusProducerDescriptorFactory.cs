// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.AzureServiceBus.Producer;

/// <summary>
/// Resolves the <see cref="IServiceBusProducerDescriptor"/> to use for a given outbound message.
/// </summary>
/// <remarks>
/// The default implementation returns the descriptor registered via
/// <see cref="AzureServiceBusMessagingOptions.ConfigureCustomProducer{T}"/> when one matches the message
/// type, and falls back to the global topic path otherwise.
/// </remarks>
public interface IServiceBusProducerDescriptorFactory
{
    /// <summary>
    /// Returns the producer descriptor that should handle <paramref name="transportMessage"/>.
    /// </summary>
    /// <param name="transportMessage">The outbound transport message to resolve a producer for.</param>
    IServiceBusProducerDescriptor CreateProducerForMessage(TransportMessage transportMessage);
}
