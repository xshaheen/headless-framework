// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;

namespace Headless.Messaging.Transport;

/// <summary>
/// Defines the transport layer for sending messages to a message broker.
/// </summary>
public interface ITransport : IAsyncDisposable
{
    /// <summary>
    /// Gets the broker address information.
    /// </summary>
    BrokerAddress BrokerAddress { get; }

    /// <summary>
    /// Sends a transport message asynchronously.
    /// </summary>
    /// <param name="message">The transport message to send.</param>
    /// <param name="cancellationToken">Token to cancel the send operation.</param>
    /// <returns>A task that returns the operation result.</returns>
    Task<OperateResult> SendAsync(TransportMessage message, CancellationToken cancellationToken = default);
}
