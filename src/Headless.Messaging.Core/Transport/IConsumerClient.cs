// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Transport;

/// <summary>Message queue consumer client interface that defines operations for consuming messages from various message brokers</summary>
/// <remarks>
/// <b>Evolution policy:</b> this is a transport-provider extension point, so it evolves additively.
/// New members ship as default interface methods with a behavior-preserving fallback (see
/// <see cref="ShutdownAsync"/> and <see cref="WaitUntilReadyAsync"/>); existing member signatures
/// do not change within a major version, so custom transport implementations keep compiling
/// across minor releases and may override new defaults when they can do better.
/// </remarks>
[PublicAPI]
public interface IConsumerClient : IAsyncDisposable
{
    /// <summary>
    /// Shuts down the consumer client while bounding provider-specific in-flight work.
    /// </summary>
    /// <remarks>
    /// Implementations with their own drain stage should honor <paramref name="timeout"/>. The default
    /// delegates to <see cref="IAsyncDisposable.DisposeAsync"/> for providers without an in-flight drain.
    /// The messaging core also bounds how long it waits for this operation.
    /// </remarks>
    /// <param name="timeout">The remaining end-to-end messaging shutdown budget.</param>
    /// <param name="cancellationToken">
    /// Reserved for API consistency. Implementations must complete or safely detach cleanup even when cancellation is requested.
    /// </param>
    ValueTask ShutdownAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        return DisposeAsync();
    }

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
    /// <param name="sender">
    /// The transport-specific settlement token for the message being committed. This is the exact
    /// opaque value the client passed as the second argument to <see cref="OnMessageCallback"/>
    /// (e.g., a Kafka consume result, a RabbitMQ delivery tag, an SQS receipt handle); callers must
    /// round-trip it unchanged and must not interpret it. <see langword="null"/> when the transport
    /// needs no per-message token, in which case implementations settle their current delivery.
    /// </param>
    /// <param name="cancellationToken">Token to cancel the commit operation. Implementations may treat commit as must-complete.</param>
    /// <returns>A task that represents the asynchronous commit operation</returns>
    ValueTask CommitAsync(object? sender, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rejects the message and optionally returns it to the queue for reprocessing
    /// </summary>
    /// <param name="sender">
    /// The transport-specific settlement token for the message being rejected. This is the exact
    /// opaque value the client passed as the second argument to <see cref="OnMessageCallback"/>;
    /// callers must round-trip it unchanged and must not interpret it. <see langword="null"/> when
    /// the transport needs no per-message token, in which case implementations reject their
    /// current delivery.
    /// </param>
    /// <param name="cancellationToken">Token to cancel the reject operation. Implementations may treat reject as must-complete.</param>
    /// <returns>A task that represents the asynchronous reject operation</returns>
    ValueTask RejectAsync(object? sender, CancellationToken cancellationToken = default);

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
    /// Gets the callback invoked when a message is received from the broker. The second argument is
    /// the transport-specific settlement token to round-trip into <see cref="CommitAsync"/> or
    /// <see cref="RejectAsync"/>. Attached via <see cref="AttachCallbacks"/>.
    /// </summary>
    Func<TransportMessage, object?, Task>? OnMessageCallback { get; }

    /// <summary>
    /// Gets the callback invoked when logging events occur in the consumer client.
    /// Attached via <see cref="AttachCallbacks"/>.
    /// </summary>
    Action<LogMessageEventArgs>? OnLogCallback { get; }

    /// <summary>
    /// Attaches the message and log callbacks in one operation, replacing any previously attached
    /// callbacks (<see langword="null"/> detaches). Attach before <see cref="ListeningAsync"/>;
    /// swapping callbacks while the client is listening is not supported.
    /// </summary>
    /// <param name="onMessage">The callback invoked for each received message, or <see langword="null"/> for a client that does not consume (e.g., topology-only usage).</param>
    /// <param name="onLog">The callback invoked for transport log events, or <see langword="null"/> to drop them.</param>
    void AttachCallbacks(Func<TransportMessage, object?, Task>? onMessage, Action<LogMessageEventArgs>? onLog);
}
