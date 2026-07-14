// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Checks;
using Headless.Messaging.Transport;

namespace Headless.Messaging.InMemory;

/// <summary>
/// Consumer client for in-memory message queue.
/// </summary>
internal sealed class InMemoryConsumerClient : IConsumerClient
{
    // Bounds pending-message memory when a consumer is paused or slower than the publisher.
    // Publishers call AddSubscribeMessage under MemoryQueue's global lock, so overflow must
    // drop (TryAdd) rather than block — a blocking Add would stall every publisher.
    private const int _MaxPendingMessages = 65_536;

    private readonly MemoryQueue _queue;
    private readonly string _groupId;
    private readonly IntentType _intentType;
    private readonly byte _groupConcurrent;
    private readonly BlockingCollection<TransportMessage> _messageQueue = new(_MaxPendingMessages);
    private readonly SemaphoreSlim _semaphore;
    private readonly ConsumerPauseGate _pauseGate = new();
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _disposed;

    /// <summary>
    /// Initializes a new instance of the InMemoryConsumerClient class.
    /// </summary>
    /// <param name="queue">The in-memory queue instance</param>
    /// <param name="groupId">The consumer group ID</param>
    /// <param name="groupConcurrent">The concurrency level for the group</param>
    public InMemoryConsumerClient(
        MemoryQueue queue,
        string groupId,
        byte groupConcurrent,
        IntentType intentType = IntentType.Bus
    )
    {
        _queue = queue;
        _groupId = groupId;
        _intentType = intentType;
        _groupConcurrent = groupConcurrent;
        _semaphore = new SemaphoreSlim(groupConcurrent);
        _queue.RegisterConsumerClient(intentType, groupId, this);
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
    /// Subscribes to the specified message names.
    /// </summary>
    /// <param name="messageNames">The list of message names to subscribe to</param>
    /// <returns>A completed task</returns>
    /// <exception cref="ArgumentNullException">Thrown when messageNames is null</exception>
    public ValueTask SubscribeAsync(IEnumerable<string> messageNames, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNull(messageNames);
        cancellationToken.ThrowIfCancellationRequested();
        _queue.Subscribe(_intentType, _groupId, messageNames);
        _ready.TrySetResult();

        return ValueTask.CompletedTask;
    }

    public ValueTask WaitUntilReadyAsync(CancellationToken cancellationToken = default)
    {
        return new ValueTask(_ready.Task.WaitAsync(cancellationToken));
    }

    /// <summary>
    /// Drains all pending messages from the internal queue without processing them.
    /// </summary>
    public void DrainPendingMessages()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        try
        {
            while (_messageQueue.TryTake(out _)) { }
        }
        catch (ObjectDisposedException) when (Volatile.Read(ref _disposed) != 0)
        {
            // Disposed concurrently — nothing left to drain.
        }
    }

    /// <summary>
    /// Adds a message to the message queue for processing. When the pending queue is full the
    /// message is dropped and reported through <see cref="OnLogCallback"/>.
    /// </summary>
    /// <param name="message">The transport message to add</param>
    public void AddSubscribeMessage(TransportMessage message)
    {
        if (!_messageQueue.TryAdd(message))
        {
            OnLogCallback?.Invoke(
                new LogMessageEventArgs
                {
                    LogType = MqLogType.ConsumeError,
                    Reason =
                        $"Pending message queue for group '{_groupId}' is full ({_MaxPendingMessages}); message '{message.Name}' dropped.",
                }
            );
        }
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
                await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                _ObserveBackgroundFault(
                    Task.Run(
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
                        CancellationToken.None // Ensure semaphore release even if cancellation is requested during handler execution
                    )
                );
            }
            else
            {
                if (OnMessageCallback is not null)
                {
                    try
                    {
                        await OnMessageCallback.Invoke(message, null).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        // Mirror the concurrent branch: log and continue so a single bad message
                        // does not poison the in-memory listener loop.
                        OnLogCallback?.Invoke(
                            new LogMessageEventArgs
                            {
                                LogType = MqLogType.ExceptionReceived,
                                Reason = $"Unhandled exception in message handler: {ex}",
                            }
                        );
                    }
                }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    /// <summary>
    /// Commits the processing of a message.
    /// </summary>
    /// <param name="sender">The sender object</param>
    /// <param name="cancellationToken">Unused; the in-memory transport settles synchronously.</param>
    /// <returns>A completed task</returns>
    public ValueTask CommitAsync(object? sender, CancellationToken cancellationToken = default)
    {
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Rejects the processing of a message.
    /// </summary>
    /// <param name="sender">The sender object</param>
    /// <param name="cancellationToken">Unused; the in-memory transport settles synchronously.</param>
    /// <returns>A completed task</returns>
    public ValueTask RejectAsync(object? sender, CancellationToken cancellationToken = default)
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

    private void _ObserveBackgroundFault(Task task)
    {
        _ = task.ContinueWith(
            completedTask =>
            {
                var exception = completedTask.Exception?.GetBaseException();
                if (exception is not null)
                {
                    OnLogCallback?.Invoke(
                        new LogMessageEventArgs
                        {
                            LogType = MqLogType.ExceptionReceived,
                            Reason = $"Unhandled exception in concurrent message handler: {exception}",
                        }
                    );
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
    }

    /// <inheritdoc />
    public async ValueTask PauseAsync(CancellationToken cancellationToken = default) =>
        await _pauseGate.PauseAsync().ConfigureAwait(false);

    /// <inheritdoc />
    public async ValueTask ResumeAsync(CancellationToken cancellationToken = default) =>
        await _pauseGate.ResumeAsync().ConfigureAwait(false);

    /// <summary>
    /// Disposes the consumer client and unsubscribes from the queue.
    /// </summary>
    /// <returns>A value task representing the disposal</returns>
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return ValueTask.CompletedTask;
        }

        _pauseGate.Release();
        _ready.TrySetCanceled();
        _semaphore.Dispose();
        _messageQueue.Dispose();
        _queue.Unsubscribe(_intentType, _groupId, this);
        return ValueTask.CompletedTask;
    }
}
