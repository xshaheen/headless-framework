using System.Collections.Concurrent;
using Framework.Checks;
using Framework.Messages.Internal;
using Framework.Messages.Messages;
using Framework.Messages.Transport;

namespace Framework.Messages;

/// <summary>
/// Consumer client for in-memory message queue.
/// </summary>
internal sealed class InMemoryConsumerClient : IConsumerClient
{
    private readonly InMemoryQueue _queue;
    private readonly string _groupId;
    private readonly byte _groupConcurrent;
    private readonly BlockingCollection<TransportMessage> _messageQueue = new();
    private readonly SemaphoreSlim _semaphore;

    /// <summary>
    /// Initializes a new instance of the InMemoryConsumerClient class.
    /// </summary>
    /// <param name="queue">The in-memory queue instance</param>
    /// <param name="groupId">The consumer group ID</param>
    /// <param name="groupConcurrent">The concurrency level for the group</param>
    public InMemoryConsumerClient(InMemoryQueue queue, string groupId, byte groupConcurrent)
    {
        _queue = queue;
        _groupId = groupId;
        _groupConcurrent = groupConcurrent;
        _semaphore = new SemaphoreSlim(groupConcurrent);
        _queue.RegisterConsumerClient(groupId, this);
    }

    /// <summary>
    /// Gets or sets the callback function to handle received messages.
    /// </summary>
    public Func<TransportMessage, object?, Task>? OnMessageCallback { get; set; }

    /// <summary>
    /// Gets or sets the callback function for logging events.
    /// </summary>
    public Action<LogMessageEventArgs>? OnLogCallback { get; set; }

    /// <summary>
    /// Gets the broker address information.
    /// </summary>
    public BrokerAddress BrokerAddress => new("InMemory", "localhost");

    /// <summary>
    /// Subscribes to the specified topics.
    /// </summary>
    /// <param name="topics">The list of topics to subscribe to</param>
    /// <returns>A completed task</returns>
    /// <exception cref="ArgumentNullException">Thrown when topics is null</exception>
    public ValueTask SubscribeAsync(IEnumerable<string> topics)
    {
        Argument.IsNotNull(topics);
        _queue.Subscribe(_groupId, topics);

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Adds a message to the message queue for processing.
    /// </summary>
    /// <param name="message">The transport message to add</param>
    public void AddSubscribeMessage(TransportMessage message)
    {
        _messageQueue.Add(message);
    }

    /// <summary>
    /// Listens for messages with the specified timeout.
    /// </summary>
    /// <param name="timeout">The timeout for listening</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>A task representing the listening operation</returns>
    public async ValueTask ListeningAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        foreach (var message in _messageQueue.GetConsumingEnumerable(cancellationToken))
        {
            if (_groupConcurrent > 0)
            {
                await _semaphore.WaitAsync(cancellationToken);
                _ = Task.Run(() => OnMessageCallback?.Invoke(message, null) ?? Task.CompletedTask, cancellationToken)
                    .AnyContext();
            }
            else
            {
                if (OnMessageCallback is not null)
                {
                    await OnMessageCallback.Invoke(message, null).AnyContext();
                }
            }
        }
    }

    /// <summary>
    /// Commits the processing of a message.
    /// </summary>
    /// <param name="sender">The sender object</param>
    /// <returns>A completed task</returns>
    public ValueTask CommitAsync(object? sender)
    {
        _semaphore.Release();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Rejects the processing of a message.
    /// </summary>
    /// <param name="sender">The sender object</param>
    /// <returns>A completed task</returns>
    public ValueTask RejectAsync(object? sender)
    {
        _semaphore.Release();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Disposes the consumer client and unsubscribes from the queue.
    /// </summary>
    /// <returns>A value task representing the disposal</returns>
    public ValueTask DisposeAsync()
    {
        _semaphore.Dispose();
        _messageQueue.Dispose();
        _queue.Unsubscribe(_groupId);
        return ValueTask.CompletedTask;
    }
}
