// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Low-level contract implemented by every broker transport (bus or queue) to send a serialized
/// <see cref="TransportMessage"/> to the underlying broker and report the outcome.
/// </summary>
/// <remarks>
/// <c>IBusTransport</c> and <c>IQueueTransport</c> extend this interface with intent-specific
/// delivery semantics. Application code should depend on those narrower interfaces rather than
/// <see cref="ITransport"/> directly.
/// </remarks>
[PublicAPI]
public interface ITransport : IAsyncDisposable
{
    /// <summary>
    /// Gets the broker address information.
    /// </summary>
    BrokerAddress BrokerAddress { get; }

    /// <summary>
    /// Sends a serialized message to the broker and returns whether the operation succeeded.
    /// </summary>
    /// <remarks>
    /// A broker failure is reported through the returned result, not thrown. That includes a send after
    /// <see cref="IAsyncDisposable.DisposeAsync"/>: the transport returns a failed result carrying an
    /// <see cref="ObjectDisposedException"/> and does not contact the broker, even when disposal released nothing
    /// because a shared pool owns the connection.
    /// </remarks>
    /// <param name="message">The transport message, carrying headers and a serialized body.</param>
    /// <param name="cancellationToken">A token to cancel the send operation.</param>
    /// <returns>
    /// An <see cref="OperateResult"/> that indicates success or carries the failure description.
    /// </returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task<OperateResult> SendAsync(TransportMessage message, CancellationToken cancellationToken = default);
}
