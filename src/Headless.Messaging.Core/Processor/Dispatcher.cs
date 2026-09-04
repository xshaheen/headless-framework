// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using System.Threading.Channels;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Messaging.Retry;
using Headless.Messaging.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Processor;

internal sealed class Dispatcher
    : IDispatcher,
        ICommittedDelayedMessageDispatcher,
        ICommittedMessageDispatcher,
        IRetryDispatcher,
        IProcessingServerShutdown
{
    private readonly ISubscribeExecutor _executor;
    private readonly ILogger<Dispatcher> _logger;
    private readonly MessagingOptions _options;
    private readonly IMessageSender _sender;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDataStorage _storage;
    private readonly TimeProvider _timeProvider;
    private readonly IHostApplicationLifetime? _hostApplicationLifetime;
#pragma warning disable CA2213 // Disposed by deadline-bounded asynchronous finalization.
    private readonly ScheduledMediumMessageQueue _schedulerQueue;
#pragma warning restore CA2213
    private readonly bool _enableParallelExecute;
    private readonly bool _enableParallelSend;
    private readonly int _publishChannelSize;
    private readonly Lock _retryDispatchGate = new();
    private readonly Lock _shutdownGate = new();
    private bool _acceptingRetryDispatch;

#pragma warning disable CA2213 // Disposed by deadline-bounded asynchronous finalization.
    private CancellationTokenSource? _tasksCts;
#pragma warning restore CA2213
    private Task? _sendingTask;
    private Task[] _processingTasks = [];
    private Task? _schedulerTask;
    private Task? _eventualCleanupTask;
    private Task? _quiesceTask;

    // Volatile because writers (DisposeAsync, _ResetStateIfNeeded) race readers on channel-writer
    // and processing-loop threads. Without the barrier, a stale `false` read can slip past the
    // post-dispose guard in _WriteToChannelAsync and only get caught by the ObjectDisposedException
    // → OCE fallback. Volatile makes the post-dispose visibility deterministic.
    private volatile bool _disposed;

    internal Action? StartPublicationHookForTest { get; set; }

    // Pre-cancelled token used for OCEs surfaced when the dispatcher is pre-start or post-dispose.
    // Downstream code that pattern-matches on oce.CancellationToken (e.g., RetryHelper.IsCancellation)
    // sees IsCancellationRequested = true and classifies the failure as cancellation rather than
    // a non-cancellation fault.
    private static readonly CancellationToken _DispatcherStoppedToken = new(canceled: true);

    private CancellationTokenSource TasksCts =>
        _tasksCts ?? throw new InvalidOperationException("Dispatcher is not started.");

    private Channel<PublishedDispatchWork> PublishedChannel
    {
        get => field ?? throw new InvalidOperationException("Published channel is not initialized.");
        set;
    }

    private Channel<ReceivedDispatchWork> ReceivedChannel
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
        TimeProvider timeProvider,
        IServiceScopeFactory scopeFactory,
        IHostApplicationLifetime? hostApplicationLifetime = null
    )
    {
        _logger = logger;
        _sender = sender;
        _options = options.Value;
        _executor = executor;
        _storage = storage;
        _timeProvider = timeProvider;
        _scopeFactory = scopeFactory;
        _hostApplicationLifetime = hostApplicationLifetime;
        _schedulerQueue = new ScheduledMediumMessageQueue(timeProvider, _options.SchedulerBatchSize);
        _enableParallelExecute = _options.EnableSubscriberParallelExecute;
        _enableParallelSend = _options.EnablePublishParallelSend;
        _publishChannelSize = Environment.ProcessorCount * 500;
    }

    #region Public Methods

    public ValueTask StartAsync(CancellationToken stoppingToken)
    {
        lock (_shutdownGate)
        {
            _ResetStateIfNeeded();
            StartPublicationHookForTest?.Invoke();

            stoppingToken.ThrowIfCancellationRequested();
            _tasksCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, CancellationToken.None);

            _InitializePublishedChannel();
            _StartSendingTask();

            if (_enableParallelExecute)
            {
                _InitializeReceivedChannel();
                _StartProcessingTasks();
            }

            _schedulerTask = _StartSchedulerTaskAsync();
            lock (_retryDispatchGate)
            {
                _acceptingRetryDispatch = true;
            }
            _ = _schedulerTask.ContinueWith(
                _OnSchedulerLoopFaulted,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default
            );
        }

        return ValueTask.CompletedTask;
    }

    private void _OnSchedulerLoopFaulted(Task task)
    {
        if (task.Exception is { } ex)
        {
            _SignalLoopTermination("scheduler", ex);
        }
    }

    /// <summary>
    /// Surfaces a terminal loop fault to operators and to the host. The diagnostic is logged at
    /// Critical level (a dispatcher loop has died and cannot be restarted in-place; published
    /// channel backpressure will eventually block <see cref="EnqueueToPublish"/> forever otherwise),
    /// then <see cref="IHostApplicationLifetime.StopApplication"/> is requested so process supervisors
    /// (Kubernetes, systemd, IIS) trigger a clean restart instead of leaving a "healthy" host with
    /// a dead message pipeline.
    /// </summary>
    private void _SignalLoopTermination(string loopName, Exception exception)
    {
        _logger.DispatcherLoopFaultedAndTerminated(exception, loopName);

        try
        {
            _hostApplicationLifetime?.StopApplication();
        }
        catch (Exception stopEx)
        {
            // StopApplication may throw if the host is already shutting down; log and absorb so
            // the fault continuation does not propagate to the thread-pool unobserved exception
            // hook (which would crash the process for a benign double-signal).
            _logger.DispatcherLoopStopApplicationFailed(stopEx, loopName);
        }
    }

    public async Task EnqueueToScheduler(
        MediumMessage message,
        DateTimeOffset publishTime,
        DbTransaction? transaction = null,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        message.ExpiresAt = publishTime;

        var timeSpan = publishTime - _timeProvider.GetUtcNow();
        var statusName = timeSpan <= TimeSpan.FromMinutes(1) ? StatusName.Queued : StatusName.Delayed;

        var changed = await _storage
            .ChangePublishStateAsync(
                message,
                statusName,
                transaction: transaction,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);

        if (!changed)
        {
            return;
        }

        if (statusName == StatusName.Queued)
        {
            if (!_schedulerQueue.TryEnqueue(message, publishTime.Ticks))
            {
                await _storage
                    .ChangePublishStateAsync(
                        message,
                        StatusName.Delayed,
                        transaction: transaction,
                        cancellationToken: CancellationToken.None
                    )
                    .ConfigureAwait(false);
            }
        }
    }

    void ICommittedDelayedMessageDispatcher.EnqueueCommittedDelayedMessage(MediumMessage message)
    {
        if (_IsCancellationRequested())
        {
            _logger.MessagePersistButSystemStopped();
            return;
        }

        if (message.ExpiresAt is not { } publishTime)
        {
            throw new InvalidOperationException("A committed delayed message must have an expiration time.");
        }

        // A full in-memory queue is safe here: the committed message remains durable as Delayed work.
        _ = _schedulerQueue.TryEnqueue(message, publishTime.Ticks);
    }

    void ICommittedMessageDispatcher.EnqueueCommittedMessage(MediumMessage message)
    {
        if (_IsCancellationRequested())
        {
            _logger.MessagePersistButSystemStopped();
            return;
        }

        // A full channel is not a publish failure: the row already committed, so the relay is the
        // recovery authority. This path must never wait for channel capacity or broker progress.
        _ = PublishedChannel.Writer.TryWrite(new PublishedDispatchWork(message, RetryAttempt: null));
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
                await _WriteToChannelAsync(
                        PublishedChannel,
                        new PublishedDispatchWork(message, RetryAttempt: null),
                        cancellationToken
                    )
                    .ConfigureAwait(false);
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
                await _WriteToChannelAsync(
                        ReceivedChannel,
                        new ReceivedDispatchWork(message, descriptor, RetryAttempt: null),
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }
            else
            {
                // Per-message scope: scoped services resolved during ExecuteAsync (consumer, middleware,
                // user OnExhausted callback) all share the same scope instance for this message.
                await using var dispatchScope = _scopeFactory.CreateAsyncScope();
                await _executor
                    .ExecuteAsync(message, dispatchScope.ServiceProvider, descriptor, TasksCts.Token)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (Exception e)
        {
            _logger.SubscriberInvocationFailed(e, message.StorageId);
        }
    }

    async ValueTask IRetryDispatcher.DispatchPublishedAsync(MediumMessage message, CancellationToken cancellationToken)
    {
        var attempt = RetryDispatchAttempt.TryCreate(_storage, MessageType.Publish, message);
        if (attempt is null)
        {
            await EnqueueToPublish(message, cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_IsCancellationRequested())
            {
                await attempt.AbandonClaimedAsync().ConfigureAwait(false);
                return;
            }

            if (_ShouldUseParallelSend(message))
            {
                await _WritePublishedRetryAsync(message, attempt, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (!await _TryStartRetryAsync(attempt).ConfigureAwait(false))
            {
                return;
            }

            var executionState = new RetryExecutionState();
            try
            {
                await _SendMessageDirectlyAsync(message, executionState).ConfigureAwait(false);
            }
            finally
            {
                await attempt.CompleteAsync(executionState.LeaseClearedByTransition).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            await attempt.AbandonClaimedAsync().ConfigureAwait(false);
        }
    }

    ValueTask<bool> IRetryDispatcher.DispatchReceivedAsync(MediumMessage message, CancellationToken cancellationToken)
    {
        return ((IRetryDispatcher)this).DispatchReceivedAsync(
            message,
            onAbandonedBeforeExecution: null,
            cancellationToken
        );
    }

    async ValueTask<bool> IRetryDispatcher.DispatchReceivedAsync(
        MediumMessage message,
        Action? onAbandonedBeforeExecution,
        CancellationToken cancellationToken
    )
    {
        var attempt = RetryDispatchAttempt.TryCreate(
            _storage,
            MessageType.Subscribe,
            message,
            onAbandonedBeforeExecution
        );
        if (attempt is null)
        {
            await EnqueueToExecute(message, descriptor: null, cancellationToken).ConfigureAwait(false);
            return true;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_IsCancellationRequested())
            {
                await attempt.AbandonClaimedAsync().ConfigureAwait(false);
                return false;
            }

            if (_ShouldUseParallelExecute(message))
            {
                return await _WriteReceivedRetryAsync(message, attempt, cancellationToken).ConfigureAwait(false);
            }

            if (!await _TryStartRetryAsync(attempt).ConfigureAwait(false))
            {
                return false;
            }

            var executionState = new RetryExecutionState();
            try
            {
                await using var dispatchScope = _scopeFactory.CreateAsyncScope();
                await _executor
                    .ExecuteRetryAsync(
                        message,
                        dispatchScope.ServiceProvider,
                        executionState,
                        descriptor: null,
                        TasksCts.Token
                    )
                    .ConfigureAwait(false);
            }
            finally
            {
                await attempt.CompleteAsync(executionState.LeaseClearedByTransition).ConfigureAwait(false);
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            await attempt.AbandonClaimedAsync().ConfigureAwait(false);
            return false;
        }
        catch (Exception e)
        {
            _logger.SubscriberInvocationFailed(e, message.StorageId);
            return false;
        }
    }

    public ValueTask DisposeAsync()
    {
        return DisposeAsync(_options.ShutdownTimeout);
    }

    void IProcessingServerShutdown.Quiesce()
    {
        _Quiesce();
    }

    ValueTask IProcessingServerShutdown.StopAsync(TimeSpan timeout)
    {
        return DisposeAsync(timeout, CancellationToken.None);
    }

    public async ValueTask DisposeAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        _Quiesce();

        Task shutdownTask;
        TaskCompletionSource? shutdownCompletion = null;
        lock (_shutdownGate)
        {
            if (_eventualCleanupTask is { } existingShutdownTask)
            {
                shutdownTask = existingShutdownTask;
            }
            else
            {
                shutdownCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                shutdownTask = shutdownCompletion.Task;
                _eventualCleanupTask = shutdownTask;
            }
        }

        if (shutdownCompletion is not null)
        {
            _ = _CompleteShutdownAsync(shutdownCompletion);
        }

        await _WaitForShutdownAsync(shutdownTask, timeout).ConfigureAwait(false);
    }

    private void _Quiesce()
    {
        lock (_shutdownGate)
        {
            lock (_retryDispatchGate)
            {
                _acceptingRetryDispatch = false;
            }

            _disposed = true;
            if (_tasksCts is not null)
            {
                _quiesceTask ??= _tasksCts.CancelAsync();
            }
        }
    }

#pragma warning disable VSTHRD003 // The shared cleanup task is explicitly deadline-bounded and keeps running on timeout.
    private async Task _WaitForShutdownAsync(Task shutdownTask, TimeSpan timeout)
    {
        if (shutdownTask.IsCompleted)
        {
            await shutdownTask.ConfigureAwait(false);
            return;
        }

        if (timeout <= TimeSpan.Zero)
        {
            _LogShutdownTimeout();
            return;
        }

        try
        {
            await shutdownTask.WaitAsync(timeout, _timeProvider, CancellationToken.None).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            _logger.ProcessorStopFailed(ex, nameof(Dispatcher));
        }
    }
#pragma warning restore VSTHRD003

    private async Task _FinalizeShutdownAsync()
    {
        // Flush after the scheduler task has observed cancellation, so no concurrent consumer can
        // remove and publish a row while this shutdown path is moving remaining queued ids back to Delayed.
        await _FlushSchedulerQueueAsync().ConfigureAwait(false);

        if (_tasksCts is not null)
        {
            await castAndDispose(_tasksCts).ConfigureAwait(false);
        }

        await castAndDispose(_schedulerQueue).ConfigureAwait(false);

        static async ValueTask castAndDispose(IDisposable resource)
        {
            if (resource is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                resource.Dispose();
            }
        }
    }

    private async Task _FlushSchedulerQueueAsync()
    {
        try
        {
            if (_schedulerQueue.Count == 0)
            {
                return;
            }

            var messageIds = _schedulerQueue.UnorderedItems.Select(x => x.StorageId).ToArray();
            await _storage.ChangePublishStateToDelayedAsync(messageIds).ConfigureAwait(false);
            _logger.DelayedStorageUpdateSuccess();
        }
        catch (Exception e)
        {
            _logger.DelayedStorageUpdateFailed(e);
        }
    }

    #endregion

    #region Initialization Methods

    /// <summary>
    /// Resets <see cref="_tasksCts"/> and <see cref="_disposed"/> so the dispatcher can be
    /// re-started after a previous <c>StopAsync</c>/<c>DisposeAsync</c> cycle. Hosts that
    /// re-host the underlying <c>IHostedService</c> (test harnesses, dashboard hot-restart
    /// flows) call <see cref="StartAsync"/> again on the same instance; without this reset
    /// the second start would observe the cancelled token or the post-dispose flag and refuse
    /// to bring the channels back up.
    /// </summary>
    /// <remarks>
    /// This is the only call site that flips <see cref="_disposed"/> back to <see langword="false"/>.
    /// All other consumers must treat the dispatcher as terminally disposed once
    /// <see cref="DisposeAsync()"/> has run.
    /// </remarks>
    private void _ResetStateIfNeeded()
    {
        if (_disposed || _tasksCts is { IsCancellationRequested: true })
        {
            // A lagging eventual cleanup (e.g. after a timed-out DisposeAsync) still references the
            // shutting-down generation's state; resetting fields underneath it would let the old
            // cleanup clear and dispose the fresh generation's tasks/CTS/scheduler when it resumes.
            if (_eventualCleanupTask is { IsCompleted: false })
            {
                throw new InvalidOperationException("Dispatcher shutdown is still in progress.");
            }

            if (
                _sendingTask is { IsCompleted: false }
                || _processingTasks.Any(static task => !task.IsCompleted)
                || _schedulerTask is { IsCompleted: false }
            )
            {
                throw new InvalidOperationException("Dispatcher shutdown is still in progress.");
            }

            _tasksCts?.Dispose();
            _tasksCts = null;
            _sendingTask = null;
            _processingTasks = [];
            _schedulerTask = null;
            _eventualCleanupTask = null;
            _quiesceTask = null;
            _disposed = false;
        }
    }

    private void _InitializePublishedChannel()
    {
        PublishedChannel = Channel.CreateBounded<PublishedDispatchWork>(
            new BoundedChannelOptions(_publishChannelSize)
            {
                AllowSynchronousContinuations = false,
                SingleReader = true,
                // Public publishes, relay pickup, and post-commit acceleration can all write concurrently.
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
            }
        );
    }

    private void _InitializeReceivedChannel()
    {
        var bufferSize = checked(
            _options.SubscriberParallelExecuteThreadCount * _options.SubscriberParallelExecuteBufferFactor
        );
        var isSingleReader = _options.SubscriberParallelExecuteThreadCount == 1;

        ReceivedChannel = Channel.CreateBounded<ReceivedDispatchWork>(
            new BoundedChannelOptions(bufferSize)
            {
                AllowSynchronousContinuations = true,
                SingleReader = isSingleReader,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
            }
        );
    }

    #endregion

    #region Task Startup Methods

    private void _StartSendingTask()
    {
        // Fire-and-forget the sending loop on the thread pool, but attach a fault continuation so
        // unobserved exceptions surface in logs AND signal host shutdown (R2). Using `async Task`
        // (changed from `async ValueTask`) ensures Task.Run picks the unwrapping overload, so the
        // returned Task tracks the loop's lifetime rather than completing the moment the ValueTask
        // struct is returned.
        //
        // The fault path MUST notify the host. A non-OCE exception that kills the sending loop
        // would otherwise leave PublishedChannel filling indefinitely (BoundedChannelFullMode.Wait)
        // and every subsequent EnqueueToPublish would block forever while the host still reports
        // "healthy".
        _sendingTask = Task.Run(_SendingAsync, TasksCts.Token);
        _ = _sendingTask.ContinueWith(
            t => _SignalLoopTermination("sending", t.Exception!),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default
        );
    }

    private void _StartProcessingTasks()
    {
        // Fire-and-forget per-thread processing loops; faults are signalled to the host (R2)
        // via _SignalLoopTermination. A dead processing loop would leave ReceivedChannel filling
        // and EnqueueToExecute would block forever, masking the failure from the host.
        _processingTasks =
        [
            .. Enumerable
                .Range(0, _options.SubscriberParallelExecuteThreadCount)
                .Select(_ => Task.Run(_ProcessingAsync, TasksCts.Token)),
        ];

        foreach (var loop in _processingTasks)
        {
            _ = loop.ContinueWith(
                t => _SignalLoopTermination("processing", t.Exception!),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default
            );
        }
    }

    private Task _StartSchedulerTaskAsync()
    {
        return Task.Run(
            async () =>
            {
                while (!TasksCts.Token.IsCancellationRequested)
                {
                    try
                    {
                        await _ProcessScheduledMessagesAsync().ConfigureAwait(false);
                        await _timeProvider.Delay(TimeSpan.FromMilliseconds(100), TasksCts.Token).ConfigureAwait(false);
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

    private List<Task> _GetBackgroundTasks()
    {
        var tasks = new List<Task>(_processingTasks.Length + 2);
        if (_sendingTask is { } sendingTask)
        {
            tasks.Add(sendingTask);
        }

        tasks.AddRange(_processingTasks);
        if (_schedulerTask is { } schedulerTask)
        {
            tasks.Add(schedulerTask);
        }

        return tasks;
    }

    private async Task _CompleteShutdownAsync(TaskCompletionSource completion)
    {
        try
        {
            // Snapshot the shutting-down generation's CTS and background tasks before any await so
            // this cleanup never observes a fresh generation's state through the live fields if it
            // resumes after a restart (the _ResetStateIfNeeded guard makes that unreachable, but the
            // snapshot also removes the read-after-await pattern outright).
            var tasksCts = _tasksCts;
            var backgroundTasks = _GetBackgroundTasks();

            if (tasksCts is not null)
            {
                var cancellationTask = _quiesceTask ?? tasksCts.CancelAsync();
                await _AbandonUnreadRetryWorkAsync().ConfigureAwait(false);

#pragma warning disable VSTHRD003 // The cancellation task is deliberately completed during eventual cleanup.
                await cancellationTask.ConfigureAwait(false);
#pragma warning restore VSTHRD003

                await _CompleteBackgroundTasksAsync(backgroundTasks).ConfigureAwait(false);
            }

            await _ObserveFinalizationAsync(_FinalizeShutdownAsync()).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.ProcessorStopFailed(ex, nameof(Dispatcher));
        }
        finally
        {
            completion.TrySetResult();
        }
    }

    private async Task _CompleteBackgroundTasksAsync(IReadOnlyCollection<Task> tasks)
    {
        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown.
        }
        catch (Exception ex)
        {
            _logger.ProcessorStopFailed(ex, nameof(Dispatcher));
        }

        _ClearBackgroundTasks();
    }

#pragma warning disable VSTHRD003 // The caller-created finalization task is explicitly fault-observed here.
    private async Task _ObserveFinalizationAsync(Task finalizationTask)
    {
        try
        {
            await finalizationTask.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.ProcessorStopFailed(ex, nameof(Dispatcher));
        }
    }
#pragma warning restore VSTHRD003

    private void _LogShutdownTimeout()
    {
        _logger.ProcessorStopFailed(
            new TimeoutException("The shared messaging shutdown deadline has expired."),
            nameof(Dispatcher)
        );
    }

    private void _ClearBackgroundTasks()
    {
        _sendingTask = null;
        _processingTasks = [];
        _schedulerTask = null;
    }

    #endregion

    #region Scheduler Methods

    private async Task _ProcessScheduledMessagesAsync()
    {
        await foreach (var nextMessage in _schedulerQueue.GetConsumingEnumerable(TasksCts.Token))
        {
            TasksCts.Token.ThrowIfCancellationRequested();

            if (_ShouldUseParallelSend(nextMessage))
            {
                await _WriteToChannelAsync(PublishedChannel, new PublishedDispatchWork(nextMessage, RetryAttempt: null))
                    .ConfigureAwait(false);
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
            await using var dispatchScope = _scopeFactory.CreateAsyncScope();
            var result = await _sender.SendAsync(message, dispatchScope.ServiceProvider).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                _logger.DelayedMessageSendFailed(message.StorageId);
            }
        }
        catch (Exception ex)
        {
            _logger.ScheduledMessageSendError(ex, message.StorageId);
        }
    }

    #endregion

    #region Background Workers - Sending

    private async Task _SendingAsync()
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

        for (var i = 0; i < batchSize && PublishedChannel.Reader.TryRead(out var work); i++)
        {
            tasks.Add(_SendMessageAsync(work));
        }

        if (tasks.Count > 0)
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
    }

    private async Task _SendBatchSequentialAsync()
    {
        while (PublishedChannel.Reader.TryRead(out var work))
        {
            await _SendMessageAsync(work).ConfigureAwait(false);
        }
    }

    private async Task _SendMessageAsync(PublishedDispatchWork work)
    {
        var (message, retryAttempt) = work;
        if (retryAttempt is not null && !await _TryStartRetryAsync(retryAttempt).ConfigureAwait(false))
        {
            return;
        }

        RetryExecutionState? executionState = retryAttempt is null ? null : new RetryExecutionState();
        try
        {
            await using var dispatchScope = _scopeFactory.CreateAsyncScope();
            var result = executionState is null
                ? await _sender.SendAsync(message, dispatchScope.ServiceProvider).ConfigureAwait(false)
                : await _sender
                    .SendRetryAsync(message, dispatchScope.ServiceProvider, executionState)
                    .ConfigureAwait(false);
            if (!result.Succeeded)
            {
                _logger.MessagePublishException(result.Exception, message.Origin.Id, result.ToString());
            }
        }
        catch (Exception ex)
        {
            _logger.TransportSendError(ex, message.StorageId);
        }
        finally
        {
            if (retryAttempt is not null)
            {
                await retryAttempt.CompleteAsync(executionState!.LeaseClearedByTransition).ConfigureAwait(false);
            }
        }
    }

    private async Task _SendMessageDirectlyAsync(MediumMessage message, RetryExecutionState? executionState = null)
    {
        try
        {
            await using var dispatchScope = _scopeFactory.CreateAsyncScope();
            var result = executionState is null
                ? await _sender.SendAsync(message, dispatchScope.ServiceProvider).ConfigureAwait(false)
                : await _sender
                    .SendRetryAsync(message, dispatchScope.ServiceProvider, executionState)
                    .ConfigureAwait(false);
            if (!result.Succeeded)
            {
                _logger.MessagePublishException(result.Exception, message.Origin.Id, result.ToString());
            }
        }
        catch (Exception ex)
        {
            // Match _SendMessageAsync (parallel sibling): exceptions from the scope factory, sender
            // construction, or transport itself must not propagate to the channel-reader loop. The
            // outer EnqueueToPublish catches OCEs only — non-OCE exceptions previously escaped
            // through this method and unwound to the caller.
            _logger.TransportSendError(ex, message.StorageId);
        }
    }

    #endregion

    #region Background Workers - Processing

    private async Task _ProcessingAsync()
    {
        try
        {
            while (await ReceivedChannel.Reader.WaitToReadAsync(TasksCts.Token).ConfigureAwait(false))
            {
                while (ReceivedChannel.Reader.TryRead(out var work))
                {
                    await _ProcessReceivedMessageAsync(work).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
    }

    private async Task _ProcessReceivedMessageAsync(ReceivedDispatchWork work)
    {
        var (message, descriptor, retryAttempt) = work;
        if (retryAttempt is not null && !await _TryStartRetryAsync(retryAttempt).ConfigureAwait(false))
        {
            return;
        }

        RetryExecutionState? executionState = retryAttempt is null ? null : new RetryExecutionState();
        try
        {
            await using var dispatchScope = _scopeFactory.CreateAsyncScope();
            if (executionState is null)
            {
                await _executor
                    .ExecuteAsync(message, dispatchScope.ServiceProvider, descriptor, TasksCts.Token)
                    .ConfigureAwait(false);
            }
            else
            {
                await _executor
                    .ExecuteRetryAsync(
                        message,
                        dispatchScope.ServiceProvider,
                        executionState,
                        descriptor,
                        TasksCts.Token
                    )
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (Exception e)
        {
            _logger.SubscriberInvocationFailed(e, message.StorageId);
        }
        finally
        {
            if (retryAttempt is not null)
            {
                await retryAttempt.CompleteAsync(executionState!.LeaseClearedByTransition).ConfigureAwait(false);
            }
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

    private async ValueTask _WritePublishedRetryAsync(
        MediumMessage message,
        RetryDispatchAttempt attempt,
        CancellationToken cancellationToken
    )
    {
        _ = await _WriteRetryAsync(
                PublishedChannel,
                new PublishedDispatchWork(message, attempt),
                attempt,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private ValueTask<bool> _WriteReceivedRetryAsync(
        MediumMessage message,
        RetryDispatchAttempt attempt,
        CancellationToken cancellationToken
    )
    {
        return _WriteRetryAsync(
            ReceivedChannel,
            new ReceivedDispatchWork(message, Descriptor: null, RetryAttempt: attempt),
            attempt,
            cancellationToken
        );
    }

    private async ValueTask<bool> _WriteRetryAsync<T>(
        Channel<T> channel,
        T work,
        RetryDispatchAttempt attempt,
        CancellationToken cancellationToken
    )
    {
        if (!attempt.TryQueue())
        {
            return false;
        }

        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(TasksCts.Token, cancellationToken);
            while (true)
            {
                lock (_retryDispatchGate)
                {
                    if (!_acceptingRetryDispatch)
                    {
                        break;
                    }

                    if (channel.Writer.TryWrite(work))
                    {
                        return true;
                    }
                }

                if (await channel.Writer.WaitToWriteAsync(linkedCts.Token).ConfigureAwait(false))
                {
                    continue;
                }

                await attempt.AbandonQueuedAsync().ConfigureAwait(false);
                return false;
            }
        }
        catch
        {
            await attempt.AbandonQueuedAsync().ConfigureAwait(false);
            throw;
        }

        await attempt.AbandonQueuedAsync().ConfigureAwait(false);
        return false;
    }

    private async ValueTask<bool> _TryStartRetryAsync(RetryDispatchAttempt attempt)
    {
        lock (_retryDispatchGate)
        {
            if (_acceptingRetryDispatch && attempt.TryStart())
            {
                return true;
            }
        }

        await attempt.AbandonAsync().ConfigureAwait(false);
        return false;
    }

    private async Task _AbandonUnreadRetryWorkAsync()
    {
        var publishedOrdinary = new List<PublishedDispatchWork>();
        var retryAttempts = new List<RetryDispatchAttempt>();
        while (PublishedChannel.Reader.TryRead(out var published))
        {
            if (published.RetryAttempt is not null)
            {
                retryAttempts.Add(published.RetryAttempt);
            }
            else
            {
                publishedOrdinary.Add(published);
            }
        }

        foreach (var ordinary in publishedOrdinary)
        {
            _ = PublishedChannel.Writer.TryWrite(ordinary);
        }

        if (!_enableParallelExecute)
        {
            await _ReleaseAbandonedAsync(retryAttempts).ConfigureAwait(false);
            return;
        }

        var receivedOrdinary = new List<ReceivedDispatchWork>();
        while (ReceivedChannel.Reader.TryRead(out var received))
        {
            if (received.RetryAttempt is not null)
            {
                retryAttempts.Add(received.RetryAttempt);
            }
            else
            {
                receivedOrdinary.Add(received);
            }
        }

        foreach (var ordinary in receivedOrdinary)
        {
            _ = ReceivedChannel.Writer.TryWrite(ordinary);
        }

        await _ReleaseAbandonedAsync(retryAttempts).ConfigureAwait(false);
    }

    private async Task _ReleaseAbandonedAsync(IEnumerable<RetryDispatchAttempt> attempts)
    {
        try
        {
            await RetryDispatchAttempt.ReleaseAbandonedBatchAsync(attempts).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.ProcessorStopFailed(ex, nameof(Dispatcher));
        }
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
        // Guard against post-dispose access. Two pre/post-dispose shapes to cover:
        //   (1) Pre-start / never-initialized:  `_tasksCts is null`
        //   (2) Post-dispose: `_disposed == true`. DisposeAsync only flips the flag and disposes
        //       the CTS; it does not null `_tasksCts`, so `_tasksCts is null` does NOT catch this.
        //       Accessing `_tasksCts.Token` after dispose throws ObjectDisposedException, which
        //       the callers (EnqueueToExecute / EnqueueToPublish) do not catch.
        // Producing an OCE in both shapes keeps the catch contract uniform: a write after dispose
        // unwinds as benign cancellation, not as an unhandled exception escaping the dispatch loop.
        if (_tasksCts is null || _disposed)
        {
            throw new OperationCanceledException(
                "Dispatcher is not started or has been disposed.",
                _DispatcherStoppedToken
            );
        }

        if (!channel.Writer.TryWrite(item))
        {
            // A concurrent DisposeAsync between the guard above and CreateLinkedTokenSource below
            // can still race ObjectDisposedException out of the access to _tasksCts.Token.
            // Convert any such race to OCE so the catch contract holds end-to-end.
            CancellationTokenSource linkedCts;
            try
            {
                linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_tasksCts.Token, cancellationToken);
            }
            catch (ObjectDisposedException)
            {
                throw new OperationCanceledException("Dispatcher was disposed during write.", _DispatcherStoppedToken);
            }

            try
            {
                while (await channel.Writer.WaitToWriteAsync(linkedCts.Token).ConfigureAwait(false))
                {
                    if (channel.Writer.TryWrite(item))
                    {
                        break;
                    }
                }
            }
            finally
            {
                linkedCts.Dispose();
            }
        }
    }

    #endregion

    private readonly record struct PublishedDispatchWork(MediumMessage Message, RetryDispatchAttempt? RetryAttempt);

    private readonly record struct ReceivedDispatchWork(
        MediumMessage Message,
        ConsumerExecutorDescriptor? Descriptor,
        RetryDispatchAttempt? RetryAttempt
    );
}
