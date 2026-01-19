// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Threading.Channels;
using Framework.Messages.Configuration;
using Framework.Messages.Internal;
using Framework.Messages.Messages;
using Framework.Messages.Monitoring;
using Framework.Messages.Persistence;
using Framework.Messages.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Framework.Messages.Processor;

public class Dispatcher : IDispatcher
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
    private Channel<MediumMessage> _publishedChannel = default!;
    private Channel<(MediumMessage, ConsumerExecutorDescriptor?)> _receivedChannel = default!;
    private bool _disposed;

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

    public async Task EnqueueToScheduler(MediumMessage message, DateTime publishTime, object? transaction = null)
    {
        message.ExpiresAt = publishTime;

        var timeSpan = publishTime - _timeProvider.GetUtcNow().UtcDateTime;
        var statusName = timeSpan <= TimeSpan.FromMinutes(1) ? StatusName.Queued : StatusName.Delayed;

        await _storage.ChangePublishStateAsync(message, statusName, transaction).ConfigureAwait(false);

        if (statusName == StatusName.Queued)
        {
            _schedulerQueue.Enqueue(message, publishTime.Ticks);
        }
    }

    public async ValueTask EnqueueToPublish(MediumMessage message)
    {
        try
        {
            if (_IsCancellationRequested())
            {
                _logger.LogWarning(
                    "The message has been persisted, but the messaging system is currently stopped. It will be attempted to be sent once the system becomes available."
                );
                return;
            }

            if (_ShouldUseParallelSend(message))
            {
                await _WriteToChannelAsync(_publishedChannel, message).ConfigureAwait(false);
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

    public async ValueTask EnqueueToExecute(MediumMessage message, ConsumerExecutorDescriptor? descriptor = null)
    {
        try
        {
            if (_IsCancellationRequested())
            {
                return;
            }

            if (_ShouldUseParallelExecute(message))
            {
                await _WriteToChannelAsync(_receivedChannel, (message, descriptor)).ConfigureAwait(false);
            }
            else
            {
                await _executor.ExecuteAsync(message, descriptor, _tasksCts!.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (Exception e)
        {
            _logger.LogError(e, "An exception occurred when invoke subscriber. MessageId:{MessageId}", message.DbId);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _tasksCts?.Dispose();
    }

    #endregion

    #region Initialization Methods

    private void _ResetStateIfNeeded()
    {
        if (_disposed || (_tasksCts != null && _tasksCts.IsCancellationRequested))
        {
            _tasksCts?.Dispose();
            _tasksCts = null;
            _disposed = false;
        }
    }

    private void _InitializePublishedChannel()
    {
        _publishedChannel = Channel.CreateBounded<MediumMessage>(
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

        _receivedChannel = Channel.CreateBounded<(MediumMessage, ConsumerExecutorDescriptor?)>(
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
        await Task.Run(_SendingAsync, _tasksCts!.Token).ConfigureAwait(false);
    }

    private async Task _StartProcessingTasksAsync()
    {
        var processingTasks = Enumerable
            .Range(0, _options.SubscriberParallelExecuteThreadCount)
            .Select(_ => Task.Run(_ProcessingAsync, _tasksCts!.Token))
            .ToArray();

        await Task.WhenAll(processingTasks).ConfigureAwait(false);
    }

    private Task _StartSchedulerTaskAsync()
    {
        return Task.Run(
            async () =>
            {
                _RegisterSchedulerCancellationHandler();

                while (!_tasksCts!.Token.IsCancellationRequested)
                {
                    try
                    {
                        await _ProcessScheduledMessagesAsync().ConfigureAwait(false);
                        _tasksCts.Token.WaitHandle.WaitOne(100);
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected during shutdown
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            ex,
                            "Delay message publishing failed unexpectedly, which will stop future scheduled "
                                + "messages from publishing. See more details here: https://github.com/dotnetcore/CAP/issues/1637. "
                                + "Exception: {Message}",
                            ex.Message
                        );
                        throw;
                    }
                }
            },
            _tasksCts!.Token
        );
    }

    #endregion

    #region Scheduler Methods

    private void _RegisterSchedulerCancellationHandler()
    {
        _tasksCts!.Token.Register(() =>
        {
            try
            {
                if (_schedulerQueue.Count == 0)
                {
                    return;
                }

                var messageIds = _schedulerQueue.UnorderedItems.Select(x => x.DbId).ToArray();
                _storage.ChangePublishStateToDelayedAsync(messageIds).ConfigureAwait(false).GetAwaiter().GetResult();
                _logger.LogDebug("Update storage to delayed success of delayed message in memory queue!");
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Update storage fails of delayed message in memory queue!");
            }
        });
    }

    private async Task _ProcessScheduledMessagesAsync()
    {
        await foreach (var nextMessage in _schedulerQueue.GetConsumingEnumerable(_tasksCts!.Token))
        {
            _tasksCts.Token.ThrowIfCancellationRequested();

            if (_ShouldUseParallelSend(nextMessage))
            {
                await _WriteToChannelAsync(_publishedChannel, nextMessage).ConfigureAwait(false);
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
                _logger.LogError("Delay message sending failed. MessageId: {MessageId} ", message.DbId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending scheduled message. MessageId: {MessageId}", message.DbId);
        }
    }

    #endregion

    #region Background Workers - Sending

    private async ValueTask _SendingAsync()
    {
        try
        {
            while (await _publishedChannel.Reader.WaitToReadAsync(_tasksCts!.Token).ConfigureAwait(false))
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
        var batchSize = Math.Max(1, _publishChannelSize / 50);
        var tasks = new List<Task>(batchSize);

        for (var i = 0; i < batchSize && _publishedChannel.Reader.TryRead(out var message); i++)
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
        while (_publishedChannel.Reader.TryRead(out var message))
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
                _logger.MessagePublishException(message.Origin.GetId(), result.ToString(), result.Exception);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "An exception occurred when sending a message to the transport. Id:{MessageId}",
                message.DbId
            );
        }
    }

    private async Task _SendMessageDirectlyAsync(MediumMessage message)
    {
        var result = await _sender.SendAsync(message).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            _logger.MessagePublishException(message.Origin.GetId(), result.ToString(), result.Exception);
        }
    }

    #endregion

    #region Background Workers - Processing

    private async ValueTask _ProcessingAsync()
    {
        try
        {
            while (await _receivedChannel.Reader.WaitToReadAsync(_tasksCts!.Token).ConfigureAwait(false))
            {
                while (_receivedChannel.Reader.TryRead(out var messageData))
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
            await _executor.ExecuteAsync(message, descriptor, _tasksCts!.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (Exception e)
        {
            _logger.LogError(
                e,
                "An exception occurred when invoke subscriber. MessageId:{MessageId}",
                messageData.Item1.DbId
            );
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

    private async ValueTask _WriteToChannelAsync<T>(Channel<T> channel, T item)
    {
        if (!channel.Writer.TryWrite(item))
        {
            while (await channel.Writer.WaitToWriteAsync(_tasksCts!.Token).ConfigureAwait(false))
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
