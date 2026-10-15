// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Threading.Channels;
using Headless.Checks;
using Headless.Messaging.Internal;
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
    private readonly MessageLane _lane;
    private readonly byte _groupConcurrent;

    // Bounded with the default Wait mode so TryWrite reports a full queue; DropWrite would discard silently.
    private readonly Channel<Delivery> _messageQueue = Channel.CreateBounded<Delivery>(
        new BoundedChannelOptions(_MaxPendingMessages) { FullMode = BoundedChannelFullMode.Wait }
    );

    // A redelivery delay waits real time, as a broker's would; a test passes a clock to step through it.
    private readonly TimeProvider _redeliveryClock;

    // Cancels the redelivery delays still pending when the client is disposed, so their timers do not outlive it.
    private readonly CancellationTokenSource _disposeCts = new();

    private readonly SemaphoreSlim _semaphore;
    private readonly ConsumerPauseGate _pauseGate = new();
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _disposed;

    // Bumped by every drain, so a rejected message whose delay was pending when the queue was drained is dropped too.
    private int _drainGeneration;

    /// <summary>
    /// Initializes a new instance of the InMemoryConsumerClient class.
    /// </summary>
    /// <param name="queue">The in-memory queue instance</param>
    /// <param name="groupId">The consumer group ID</param>
    /// <param name="groupConcurrent">The concurrency level for the group</param>
    /// <param name="lane">The lane the group consumes</param>
    /// <param name="redeliveryClock">
    /// The clock a rejected message's redelivery delay runs on; <see cref="TimeProvider.System"/> when omitted.
    /// </param>
    public InMemoryConsumerClient(
        MemoryQueue queue,
        string groupId,
        byte groupConcurrent,
        MessageLane lane = MessageLane.Bus,
        TimeProvider? redeliveryClock = null
    )
    {
        _redeliveryClock = redeliveryClock ?? TimeProvider.System;
        _queue = queue;
        _groupId = groupId;
        _lane = lane;
        _groupConcurrent = groupConcurrent;
        _semaphore = new SemaphoreSlim(groupConcurrent);
        _queue.RegisterConsumerClient(lane, groupId, this);
    }

    /// <summary>
    /// Gets or sets the callback function to handle received messages.
    /// </summary>
    public Func<TransportMessage, object?, Task>? OnMessageCallback { get; set; }

    /// <summary>
    /// Gets or sets the callback function for logging events.
    /// </summary>
    public Action<LogMessageEventArgs>? OnLogCallback { get; set; }

    public void AttachCallbacks(Func<TransportMessage, object?, Task>? onMessage, Action<LogMessageEventArgs>? onLog)
    {
        OnMessageCallback = onMessage;
        OnLogCallback = onLog;
    }

    /// <summary>
    /// Gets the broker address information.
    /// </summary>
    public BrokerAddress BrokerAddress => new("in_memory", "localhost");

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
        _queue.Subscribe(_lane, _groupId, messageNames);
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

        Interlocked.Increment(ref _drainGeneration);
        while (_messageQueue.Reader.TryRead(out _)) { }
    }

    /// <summary>
    /// Adds a message to the message queue for processing. When the pending queue is full the
    /// message is dropped and reported through <see cref="OnLogCallback"/>.
    /// </summary>
    /// <param name="message">The transport message to add</param>
    public void AddSubscribeMessage(TransportMessage message)
    {
        if (!_messageQueue.Writer.TryWrite(new Delivery(message, DeliveryCount: 1)))
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
    /// <param name="timeout">Unused; the loop awaits the next message and wakes on arrival or cancellation, so it never polls.</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>A task representing the listening operation</returns>
    public async ValueTask ListeningAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && Volatile.Read(ref _disposed) == 0)
        {
            Delivery delivery;
            try
            {
                delivery = await _messageQueue.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ChannelClosedException)
            {
                // Disposal completes the channel: nothing more will arrive.
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
                                // The delivery is the settlement token: a reject requeues it from there.
                                await (
                                    OnMessageCallback?.Invoke(delivery.Message, delivery) ?? Task.CompletedTask
                                ).ConfigureAwait(false);
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
                        await OnMessageCallback.Invoke(delivery.Message, delivery).ConfigureAwait(false);
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
    /// Rejects the processing of a message, which comes back to this client after a delay, as a broker redelivers a
    /// rejected message to its consumer group. The delay starts at about a second and doubles with each redelivery up
    /// to 30 seconds, so a consumer whose circuit rejects every delivery does not spin on them. A message whose delay
    /// is still pending when the client stops or its queue is drained is dropped.
    /// </summary>
    /// <param name="sender">The settlement token the delivery was handed to the callback with</param>
    /// <param name="cancellationToken">Unused; the redelivery is scheduled, not awaited.</param>
    /// <returns>A completed task</returns>
    public ValueTask RejectAsync(object? sender, CancellationToken cancellationToken = default)
    {
        // The listening loop already releases the delivery's concurrency slot when the callback returns; releasing it
        // here as well would grow the group's concurrency on every reject.
        if (sender is Delivery delivery && Volatile.Read(ref _disposed) == 0)
        {
            _ = _RedeliverAfterDelayAsync(delivery, Volatile.Read(ref _drainGeneration));
        }

        return ValueTask.CompletedTask;
    }

    private async Task _RedeliverAfterDelayAsync(Delivery delivery, int drainGeneration)
    {
        try
        {
            var delay = RejectRedeliveryDelay.For(delivery.DeliveryCount, Random.Shared);
            await Task.Delay(delay, _redeliveryClock, _disposeCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (ObjectDisposedException)
        {
            // Disposal raced the delay's start; the message goes with the client.
            return;
        }

        if (Volatile.Read(ref _drainGeneration) != drainGeneration)
        {
            return;
        }

        var redelivery = delivery with { DeliveryCount = delivery.DeliveryCount + 1 };

        // A completed channel (the client was disposed) refuses the write as well, so a late redelivery is dropped.
        if (!_messageQueue.Writer.TryWrite(redelivery) && Volatile.Read(ref _disposed) == 0)
        {
            OnLogCallback?.Invoke(
                new LogMessageEventArgs
                {
                    LogType = MqLogType.ConsumeError,
                    Reason =
                        $"Pending message queue for group '{_groupId}' is full ({_MaxPendingMessages}); rejected message '{delivery.Message.Name}' dropped instead of redelivered.",
                }
            );
        }
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
    public async ValueTask PauseAsync(CancellationToken cancellationToken = default)
    {
        await _pauseGate.PauseAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask ResumeAsync(CancellationToken cancellationToken = default)
    {
        await _pauseGate.ResumeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Disposes the consumer client and unsubscribes from the queue.
    /// </summary>
    /// <returns>A value task representing the disposal</returns>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _pauseGate.Release();
        _ready.TrySetCanceled();
        _semaphore.Dispose();
        _messageQueue.Writer.TryComplete();
        _queue.Unsubscribe(_lane, _groupId, this);
        await _disposeCts.CancelAsync().ConfigureAwait(false);
        _disposeCts.Dispose();
    }

    /// <summary>A message handed to the consumer callback, and the settlement token that callback passes back.</summary>
    /// <param name="Message">The delivered message.</param>
    /// <param name="DeliveryCount">How many times the message has been delivered, this delivery included.</param>
    private sealed record Delivery(TransportMessage Message, ulong DeliveryCount);
}
