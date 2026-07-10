// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Transport;

/// <summary>Message queue consumer client interface that defines operations for consuming messages from various message brokers</summary>
public interface IConsumerClient : IAsyncDisposable
{
    /// <summary>
    /// Gets the broker address information that this consumer is connected to
    /// </summary>
    BrokerAddress BrokerAddress { get; }

    /// <summary>
    /// Creates (if necessary) and retrieves message-name identifiers from the message broker
    /// </summary>
    /// <param name="messageNames">Names of the requested messages to fetch</param>
    /// <param name="cancellationToken">Token to cancel broker topology provisioning.</param>
    /// <returns>A collection of message-name identifiers returned by the broker</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is canceled.</exception>
    ValueTask<ICollection<string>> FetchMessageNamesAsync(
        IEnumerable<string> messageNames,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<ICollection<string>>([.. messageNames]);
    }

    /// <summary>
    /// Subscribes to a set of message names in the message broker
    /// </summary>
    /// <param name="messageNames">Collection of message-name identifiers to subscribe to</param>
    /// <param name="cancellationToken">Token to cancel broker subscription setup.</param>
    /// <returns>A task that represents the asynchronous subscribe operation</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is canceled.</exception>
    ValueTask SubscribeAsync(IEnumerable<string> messageNames, CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts listening for messages from the subscribed message names
    /// </summary>
    /// <param name="timeout">Maximum time to wait when polling for messages</param>
    /// <param name="cancellationToken">Token to cancel the listening operation</param>
    /// <returns>A task that represents the asynchronous listening operation</returns>
    ValueTask ListeningAsync(TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>
    /// Waits until the consumer is actually ready to receive messages from the transport.
    /// </summary>
    /// <remarks>
    /// Implementations should complete this only after broker-side subscriptions, push consumers,
    /// or poll loops are active enough that a message published after bootstrap will not be missed.
    /// </remarks>
    /// <param name="cancellationToken">Token to cancel the readiness wait.</param>
    ValueTask WaitUntilReadyAsync(CancellationToken cancellationToken = default)
    {
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Manually commits message offset when the message consumption is complete
    /// </summary>
    /// <param name="sender">The message or context object to commit</param>
    /// <returns>A task that represents the asynchronous commit operation</returns>
    ValueTask CommitAsync(object? sender);

    /// <summary>
    /// Rejects the message and optionally returns it to the queue for reprocessing
    /// </summary>
    /// <param name="sender">The message or context object to reject</param>
    /// <returns>A task that represents the asynchronous reject operation</returns>
    ValueTask RejectAsync(object? sender);

    /// <summary>
    /// Pauses message consumption. Idempotent — calling on an already-paused client is a no-op.
    /// In-flight messages being processed are allowed to complete naturally.
    /// No new messages are pulled from the broker after this call returns.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the pause operation.</param>
    ValueTask PauseAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Resumes message consumption. Idempotent — calling on an already-running client is a no-op.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the resume operation.</param>
    ValueTask ResumeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Callback that is invoked when a message is received from the broker
    /// </summary>
    Func<TransportMessage, object?, Task>? OnMessageCallback { get; set; }

    /// <summary>
    /// Callback that is invoked when logging events occur in the consumer client
    /// </summary>
    Action<LogMessageEventArgs>? OnLogCallback { get; set; }
}
