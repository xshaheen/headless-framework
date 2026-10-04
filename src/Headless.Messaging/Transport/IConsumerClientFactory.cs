// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Transport;

/// <summary>
/// Factory interface for creating instances of <see cref="IConsumerClient"/>.
/// </summary>
public interface IConsumerClientFactory
{
    /// <summary>
    /// Asynchronously creates a new <see cref="IConsumerClient"/> instance.
    /// </summary>
    /// <param name="request">
    /// The subscription the client opens, its concurrency and lane, and whether it competes with other processes or
    /// belongs to this process alone.
    /// </param>
    /// <param name="cancellationToken">Token to cancel consumer creation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the created <see cref="IConsumerClient"/> instance.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is canceled.</exception>
    /// <exception cref="NotSupportedException">
    /// The transport has no every-instance subscriptions and <paramref name="request"/> asks for one. The messaging core
    /// rejects such a consumer at startup, before it calls the factory.
    /// </exception>
    Task<IConsumerClient> CreateAsync(ConsumerClientRequest request, CancellationToken cancellationToken = default);
}
