using System.Collections.Concurrent;
using Headless.Checks;
using Headless.Messaging.Messages;
using Headless.Messaging.Transport;

namespace Headless.Messaging.InMemoryQueue;

/// <summary>
/// Consumer client for in-memory message queue.
/// </summary>
internal sealed class InMemoryConsumerClient : IConsumerClient
{
    private readonly MemoryQueue _queue;
    private readonly string _groupId;
    private readonly byte _groupConcurrent;
    private readonly BlockingCollection<TransportMessage> _messageQueue = new();
    private readonly SemaphoreSlim _semaphore;
    private readonly ConsumerPauseGate _pauseGate = new();
    private int _disposed;

    /// <summary>
    /// Initializes a new instance of the InMemoryConsumerClient class.
    /// </summary>
    /// <param name="queue">The in-memory queue instance</param>
    /// <param name="groupId">The consumer group ID</param>
    /// <param name="groupConcurrent">The concurrency level for the group</param>
    public InMemoryConsumerClient(MemoryQueue queue, string groupId, byte groupConcurrent)
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
        var waitMilliseconds =
            timeout == Timeout.InfiniteTimeSpan ? -1
            : timeout <= TimeSpan.Zero ? 0
            : (int)Math.Min(timeout.TotalMilliseconds, int.MaxValue);

        while (!cancellationToken.IsCancellationRequested)
        {
            TransportMessage message;
            try
            {
                if (!_messageQueue.TryTake(out message, waitMilliseconds, cancellationToken))
                {
                    continue;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException) when (Volatile.Read(ref _disposed) != 0)
            {
                break;
            }

            await _pauseGate.WaitIfPausedAsync(cancellationToken).ConfigureAwait(false);
            if (_groupConcurrent > 0)
            {
                await _semaphore.WaitAsync(cancellationToken);
                _ObserveBackgroundFault(
                    _RunConcurrentHandlerIgnoringCancellation(
                        async () =>
                        {
                            try
                            {
                                await (OnMessageCallback?.Invoke(message, null) ?? Task.CompletedTask).ConfigureAwait(
                                    false
                                );
                            }
                            finally
                            {
                                _ReleaseSemaphore();
                            }
                        },
                        cancellationToken
                    )
                );
            }
            else
            {
                if (OnMessageCallback is not null)
                {
                    await OnMessageCallback.Invoke(message, null).ConfigureAwait(false);
                }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    /// <summary>
    /// Commits the processing of a message.
    /// </summary>
    /// <param name="sender">The sender object</param>
    /// <returns>A completed task</returns>
    public ValueTask CommitAsync(object? sender)
    {
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Rejects the processing of a message.
    /// </summary>
    /// <param name="sender">The sender object</param>
    /// <returns>A completed task</returns>
    public ValueTask RejectAsync(object? sender)
    {
        return ValueTask.CompletedTask;
    }

    private void _ReleaseSemaphore()
    {
        if (_groupConcurrent > 0)
        {
            try
            {
                _semaphore.Release();
            }
            catch (SemaphoreFullException)
            {
                // Defensive: ignore over-release
            }
        }
    }

    private static Task _RunConcurrentHandlerIgnoringCancellation(
        Func<Task> handler,
        CancellationToken cancellationToken
    )
    {
        _ = cancellationToken;
        return Task.Run(handler);
    }

    private static void _ObserveBackgroundFault(Task task)
    {
        _ = task.ContinueWith(
            static t => _ = t.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
    }

    /// <inheritdoc />
    public async ValueTask PauseAsync(CancellationToken cancellationToken = default) => await _pauseGate.PauseAsync();

    /// <inheritdoc />
    public async ValueTask ResumeAsync(CancellationToken cancellationToken = default) => await _pauseGate.ResumeAsync();

    /// <summary>
    /// Disposes the consumer client and unsubscribes from the queue.
    /// </summary>
    /// <returns>A value task representing the disposal</returns>
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return ValueTask.CompletedTask;

        _pauseGate.Release();
        _semaphore.Dispose();
        _messageQueue.Dispose();
        _queue.Unsubscribe(_groupId);
        return ValueTask.CompletedTask;
    }
}
