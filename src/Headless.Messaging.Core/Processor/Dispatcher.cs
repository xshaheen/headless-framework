// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Threading.Channels;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Processor;

public sealed class Dispatcher : IDispatcher
{
    private readonly ISubscribeExecutor _executor;
    private readonly ILogger<Dispatcher> _logger;
    private readonly MessagingOptions _options;
    private readonly IMessageSender _sender;
    private readonly IDataStorage _storage;
    private readonly TimeProvider _timeProvider;
    private readonly ScheduledMediumMessageQueue _schedulerQueue;
    private readonly bool _enableParallelExecute;
    private readonly bool _enableParallelSend;
    private readonly int _publishChannelSize;

    private CancellationTokenSource? _tasksCts;
    private bool _disposed;

    private CancellationTokenSource TasksCts =>
        _tasksCts ?? throw new InvalidOperationException("Dispatcher is not started.");

    private Channel<MediumMessage> PublishedChannel
    {
        get => field ?? throw new InvalidOperationException("Published channel is not initialized.");
        set;
    }

    private Channel<(MediumMessage, ConsumerExecutorDescriptor?)> ReceivedChannel
    {
        get => field ?? throw new InvalidOperationException("Received channel is not initialized.");
        set;
    }

    public Dispatcher(
        ILogger<Dispatcher> logger,
        IMessageSender sender,
        IOptions<MessagingOptions> options,
        ISubscribeExecutor executor,
        IDataStorage storage,
        TimeProvider timeProvider
    )
    {
        _logger = logger;
        _sender = sender;
        _options = options.Value;
        _executor = executor;
        _storage = storage;
        _timeProvider = timeProvider;
        _schedulerQueue = new ScheduledMediumMessageQueue(timeProvider);
        _enableParallelExecute = _options.EnableSubscriberParallelExecute;
        _enableParallelSend = _options.EnablePublishParallelSend;
        _publishChannelSize = Environment.ProcessorCount * 500;
    }

    #region Public Methods

    public async ValueTask StartAsync(CancellationToken stoppingToken)
    {
        _ResetStateIfNeeded();

        stoppingToken.ThrowIfCancellationRequested();
        _tasksCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, CancellationToken.None);

        _InitializePublishedChannel();
        await _StartSendingTaskAsync().ConfigureAwait(false);

        if (_enableParallelExecute)
        {
            _InitializeReceivedChannel();
            await _StartProcessingTasksAsync().ConfigureAwait(false);
        }

        _ = _StartSchedulerTaskAsync().ConfigureAwait(false);
    }

    public async Task EnqueueToScheduler(
        MediumMessage message,
        DateTime publishTime,
        object? transaction = null,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        message.ExpiresAt = publishTime;

        var timeSpan = publishTime - _timeProvider.GetUtcNow().UtcDateTime;
        var statusName = timeSpan <= TimeSpan.FromMinutes(1) ? StatusName.Queued : StatusName.Delayed;

        await _storage.ChangePublishStateAsync(message, statusName, transaction).ConfigureAwait(false);

        if (statusName == StatusName.Queued)
        {
            _schedulerQueue.Enqueue(message, publishTime.Ticks);
        }
    }

    public async ValueTask EnqueueToPublish(MediumMessage message, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_IsCancellationRequested())
            {
                _logger.MessagePersistButSystemStopped();
                return;
            }

            if (_ShouldUseParallelSend(message))
            {
                await _WriteToChannelAsync(PublishedChannel, message, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await _SendMessageDirectlyAsync(message).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
    }

    public async ValueTask EnqueueToExecute(
        MediumMessage message,
        ConsumerExecutorDescriptor? descriptor = null,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_IsCancellationRequested())
            {
                return;
            }

            if (_ShouldUseParallelExecute(message))
            {
                await _WriteToChannelAsync(ReceivedChannel, (message, descriptor), cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                await _executor.ExecuteAsync(message, descriptor, TasksCts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (Exception e)
        {
            _logger.SubscriberInvocationFailed(e, message.DbId);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        if (_tasksCts is not null)
        {
            await castAndDispose(_tasksCts);
        }

        await castAndDispose(_schedulerQueue);

        _disposed = true;

        return;

        static async ValueTask castAndDispose(IDisposable resource)
        {
            if (resource is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else
            {
                resource.Dispose();
            }
        }
    }

    #endregion

    #region Initialization Methods

    private void _ResetStateIfNeeded()
    {
        if (_disposed || _tasksCts is { IsCancellationRequested: true })
        {
            _tasksCts?.Dispose();
            _tasksCts = null;
            _disposed = false;
        }
    }

    private void _InitializePublishedChannel()
    {
        PublishedChannel = Channel.CreateBounded<MediumMessage>(
            new BoundedChannelOptions(_publishChannelSize)
            {
                AllowSynchronousContinuations = true,
                SingleReader = true,
                SingleWriter = !_enableParallelSend,
                FullMode = BoundedChannelFullMode.Wait,
            }
        );
    }

    private void _InitializeReceivedChannel()
    {
        var bufferSize = _options.SubscriberParallelExecuteThreadCount * _options.SubscriberParallelExecuteBufferFactor;
        var isSingleReader = _options.SubscriberParallelExecuteThreadCount == 1;

        ReceivedChannel = Channel.CreateBounded<(MediumMessage, ConsumerExecutorDescriptor?)>(
            new BoundedChannelOptions(bufferSize)
            {
                AllowSynchronousContinuations = true,
                SingleReader = isSingleReader,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait,
            }
        );
    }

    #endregion

    #region Task Startup Methods

    private async Task _StartSendingTaskAsync()
    {
        await Task.Run(_SendingAsync, TasksCts.Token).ConfigureAwait(false);
    }

    private async Task _StartProcessingTasksAsync()
    {
        var processingTasks = Enumerable
            .Range(0, _options.SubscriberParallelExecuteThreadCount)
            .Select(_ => Task.Run(_ProcessingAsync, TasksCts.Token))
            .ToArray();

        await Task.WhenAll(processingTasks).ConfigureAwait(false);
    }

    private Task _StartSchedulerTaskAsync()
    {
        return Task.Run(
            async () =>
            {
                _RegisterSchedulerCancellationHandler();

                while (!TasksCts.Token.IsCancellationRequested)
                {
                    try
                    {
                        await _ProcessScheduledMessagesAsync().ConfigureAwait(false);
                        TasksCts.Token.WaitHandle.WaitOne(100);
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected during shutdown
                    }
                    catch (Exception ex)
                    {
                        _logger.DelayedMessagePublishFailed(ex, ex.Message);
                        throw;
                    }
                }
            },
            TasksCts.Token
        );
    }

    #endregion

    #region Scheduler Methods

    private void _RegisterSchedulerCancellationHandler()
    {
        TasksCts.Token.Register(() =>
        {
            try
            {
                if (_schedulerQueue.Count == 0)
                {
                    return;
                }

                var messageIds = _schedulerQueue.UnorderedItems.Select(x => x.DbId).ToArray();
                _storage
                    .ChangePublishStateToDelayedAsync(messageIds)
                    .AsTask()
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
                _logger.DelayedStorageUpdateSuccess();
            }
            catch (Exception e)
            {
                _logger.DelayedStorageUpdateFailed(e);
            }
        });
    }

    private async Task _ProcessScheduledMessagesAsync()
    {
        await foreach (var nextMessage in _schedulerQueue.GetConsumingEnumerable(TasksCts.Token))
        {
            TasksCts.Token.ThrowIfCancellationRequested();

            if (_ShouldUseParallelSend(nextMessage))
            {
                await _WriteToChannelAsync(PublishedChannel, nextMessage).ConfigureAwait(false);
            }
            else
            {
                await _SendScheduledMessageDirectlyAsync(nextMessage).ConfigureAwait(false);
            }
        }
    }

    private async Task _SendScheduledMessageDirectlyAsync(MediumMessage message)
    {
        try
        {
            var result = await _sender.SendAsync(message).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                _logger.DelayedMessageSendFailed(message.DbId);
            }
        }
        catch (Exception ex)
        {
            _logger.ScheduledMessageSendError(ex, message.DbId);
        }
    }

    #endregion

    #region Background Workers - Sending

    private async ValueTask _SendingAsync()
    {
        try
        {
            while (await PublishedChannel.Reader.WaitToReadAsync(TasksCts.Token).ConfigureAwait(false))
            {
                if (_enableParallelSend)
                {
                    await _SendBatchParallelAsync().ConfigureAwait(false);
                }
                else
                {
                    await _SendBatchSequentialAsync().ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
    }

    private async Task _SendBatchParallelAsync()
    {
        var batchSize = _CalculateBatchSize();
        var tasks = new List<Task>(batchSize);

        for (var i = 0; i < batchSize && PublishedChannel.Reader.TryRead(out var message); i++)
        {
            tasks.Add(_SendMessageAsync(message));
        }

        if (tasks.Count > 0)
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
    }

    private async Task _SendBatchSequentialAsync()
    {
        while (PublishedChannel.Reader.TryRead(out var message))
        {
            await _SendMessageAsync(message).ConfigureAwait(false);
        }
    }

    private async Task _SendMessageAsync(MediumMessage message)
    {
        try
        {
            var result = await _sender.SendAsync(message).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                _logger.MessagePublishException(result.Exception, message.Origin.GetId(), result.ToString());
            }
        }
        catch (Exception ex)
        {
            _logger.TransportSendError(ex, message.DbId);
        }
    }

    private async Task _SendMessageDirectlyAsync(MediumMessage message)
    {
        var result = await _sender.SendAsync(message).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            _logger.MessagePublishException(result.Exception, message.Origin.GetId(), result.ToString());
        }
    }

    #endregion

    #region Background Workers - Processing

    private async ValueTask _ProcessingAsync()
    {
        try
        {
            while (await ReceivedChannel.Reader.WaitToReadAsync(TasksCts.Token).ConfigureAwait(false))
            {
                while (ReceivedChannel.Reader.TryRead(out var messageData))
                {
                    await _ProcessReceivedMessageAsync(messageData).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
    }

    private async Task _ProcessReceivedMessageAsync((MediumMessage, ConsumerExecutorDescriptor?) messageData)
    {
        try
        {
            var (message, descriptor) = messageData;
            await _executor.ExecuteAsync(message, descriptor, TasksCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (Exception e)
        {
            _logger.SubscriberInvocationFailed(e, messageData.Item1.DbId);
        }
    }

    #endregion

    #region Helper Methods

    private bool _IsCancellationRequested()
    {
        return _tasksCts?.IsCancellationRequested ?? true;
    }

    private bool _ShouldUseParallelSend(MediumMessage message)
    {
        return _enableParallelSend && message.Retries == 0;
    }

    private bool _ShouldUseParallelExecute(MediumMessage message)
    {
        return _enableParallelExecute && message.Retries == 0;
    }

    private int _CalculateBatchSize()
    {
        // If configured explicitly, use it (clamped to valid range)
        if (_options.PublishBatchSize.HasValue)
        {
            return Math.Clamp(_options.PublishBatchSize.Value, 1, 500);
        }

        // Auto-calculate using logarithmic formula with bounds
        // Low traffic (< 1K/sec): 10-50
        // Medium traffic (1K-10K/sec): 50-200
        // High traffic (> 10K/sec): 100-500
        return Math.Min(500, Math.Max(10, (int)Math.Log2(_publishChannelSize) * 10));
    }

    private async ValueTask _WriteToChannelAsync<T>(
        Channel<T> channel,
        T item,
        CancellationToken cancellationToken = default
    )
    {
        if (!channel.Writer.TryWrite(item))
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(TasksCts.Token, cancellationToken);
            while (await channel.Writer.WaitToWriteAsync(linkedCts.Token).ConfigureAwait(false))
            {
                if (channel.Writer.TryWrite(item))
                {
                    break;
                }
            }
        }
    }

    #endregion
}
