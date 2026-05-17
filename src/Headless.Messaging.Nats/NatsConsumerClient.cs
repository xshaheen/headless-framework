// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Options;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace Headless.Messaging.Nats;

internal sealed class NatsConsumerClient(
    string name,
    byte groupConcurrent,
    IOptions<MessagingNatsOptions> options,
    IServiceProvider serviceProvider,
    Func<string, ConsumerConfig, CancellationToken, Task<INatsJSConsumer>>? consumerFactory = null
) : IConsumerClient
{
    private readonly Lock _receiveLock = new();
    private readonly MessagingNatsOptions _natsOptions = options.Value;

    private readonly SemaphoreSlim? _semaphore = groupConcurrent > 0 ? new SemaphoreSlim(groupConcurrent) : null;
    private readonly ConsumerPauseGate _pauseGate = new();
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private NatsConnection? _connection;
    private NatsJSContext? _jsContext;
    private ReceiveTokenState _receiveTokenState = new();
    private IEnumerable<string>? _subscribedTopics;
    private int _disposed;

    public Func<TransportMessage, object?, Task>? OnMessageCallback { get; set; }

    public Action<LogMessageEventArgs>? OnLogCallback { get; set; }

    public BrokerAddress BrokerAddress => new("nats", BrokerAddressDisplay.FormatMany(_natsOptions.Servers));

    public async Task ConnectAsync()
    {
        // Consumer connections disable reconnect so failures propagate to the
        // circuit breaker instead of being silently retried by the NATS client.
        var opts = _natsOptions.BuildNatsOpts() with
        {
            MaxReconnectRetry = 0,
        };

        _connection = new NatsConnection(opts);
        await _connection.ConnectAsync().ConfigureAwait(false);
        _jsContext = new NatsJSContext(_connection);
    }

    public async ValueTask<ICollection<string>> FetchTopicsAsync(IEnumerable<string> topicNames)
    {
        if (!_natsOptions.EnableSubscriberClientStreamAndSubjectCreation)
        {
            return topicNames.ToList();
        }

        // Preserve wildcard coverage for hierarchical subjects, but add exact
        // subjects for bare/non-prefix topics that the wildcard cannot match.
        var streamGroups = topicNames.GroupBy(x => _natsOptions.NormalizeStreamName(x), StringComparer.Ordinal);

        foreach (var streamGroup in streamGroups)
        {
            var config = new StreamConfig
            {
                Name = streamGroup.Key,
                Subjects = [.. BuildStreamSubjects(streamGroup.Key, streamGroup)],
                NoAck = false,
                // File storage is the production default. Override via StreamOptions
                // for dev/testing: config.Storage = StreamConfigStorage.Memory;
                Storage = StreamConfigStorage.File,
            };

            _natsOptions.StreamOptions?.Invoke(config);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await _jsContext!.CreateOrUpdateStreamAsync(config, cts.Token).ConfigureAwait(false);
        }

        return topicNames.ToList();
    }

    internal static IReadOnlyList<string> BuildStreamSubjects(string streamName, IEnumerable<string> topicNames)
    {
        Argument.IsNotNull(topicNames);

        var subjects = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var hierarchicalPrefix = streamName + ".";
        var hasHierarchicalSubjects = false;

        foreach (var topicName in topicNames)
        {
            if (!seen.Add(topicName))
            {
                continue;
            }

            if (topicName.StartsWith(hierarchicalPrefix, StringComparison.Ordinal))
            {
                hasHierarchicalSubjects = true;
                continue;
            }

            subjects.Add(topicName);
        }

        if (hasHierarchicalSubjects)
        {
            subjects.Add($"{streamName}.>");
        }

        return subjects;
    }

    public ValueTask SubscribeAsync(IEnumerable<string> topics)
    {
        Argument.IsNotNull(topics);

        _subscribedTopics = topics.ToList();

        return ValueTask.CompletedTask;
    }

    public async ValueTask ListeningAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var streamGroups = _subscribedTopics!.GroupBy(x => _natsOptions.NormalizeStreamName(x), StringComparer.Ordinal);
        var tasks = new List<Task>();
        var startupTasks = new List<Task>();

        foreach (var streamGroup in streamGroups)
        {
            var groupName = Helper.Normalized(name);

            foreach (var subject in streamGroup)
            {
                var durableName = Helper.Normalized(groupName + "-" + subject);

                var consumerConfig = new ConsumerConfig(durableName)
                {
                    FilterSubject = subject,
                    DeliverPolicy = ConsumerConfigDeliverPolicy.New,
                    AckWait = TimeSpan.FromSeconds(30),
                };

                _natsOptions.ConsumerOptions?.Invoke(consumerConfig);

                var startupReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                startupTasks.Add(startupReady.Task);
                tasks.Add(
                    _ConsumeSubjectAsync(streamGroup.Key, consumerConfig, timeout, startupReady, cancellationToken)
                );
            }
        }

        if (startupTasks.Count == 0)
        {
            _ready.TrySetResult();
        }
        else
        {
            await Task.WhenAll(startupTasks).ConfigureAwait(false);
            _ready.TrySetResult();
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    public ValueTask WaitUntilReadyAsync(CancellationToken cancellationToken = default)
    {
        return new ValueTask(_ready.Task.WaitAsync(cancellationToken));
    }

    private async Task _ConsumeSubjectAsync(
        string streamName,
        ConsumerConfig consumerConfig,
        TimeSpan timeout,
        TaskCompletionSource startupReady,
        CancellationToken cancellationToken
    )
    {
        var retryDelay = TimeSpan.FromSeconds(1);
        var nextOpts = timeout > TimeSpan.Zero ? new NatsJSNextOpts { Expires = timeout } : null;
        var readyReported = false;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var consumer = consumerFactory is not null
                    ? await consumerFactory(streamName, consumerConfig, cancellationToken).ConfigureAwait(false)
                    : await _jsContext!
                        .CreateOrUpdateConsumerAsync(streamName, consumerConfig, cancellationToken)
                        .ConfigureAwait(false);

                if (!readyReported)
                {
                    readyReported = true;
                    startupReady.TrySetResult();
                }

                while (!cancellationToken.IsCancellationRequested)
                {
                    INatsJSMsg<ReadOnlyMemory<byte>>? msg;
                    await _pauseGate.WaitIfPausedAsync(cancellationToken).ConfigureAwait(false);

                    using var receiveLease = _AcquireReceiveLease(cancellationToken);

                    try
                    {
                        msg = await consumer
                            .NextAsync(
                                serializer: NatsRawSerializer<ReadOnlyMemory<byte>>.Default,
                                opts: nextOpts,
                                cancellationToken: receiveLease.Token
                            )
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (OperationCanceledException) when (_pauseGate.IsPaused)
                    {
                        continue;
                    }
                    catch (NatsJSApiException ex)
                    {
                        OnLogCallback?.Invoke(
                            new LogMessageEventArgs
                            {
                                LogType = MqLogType.ConnectError,
                                Reason = $"JetStream API error for stream '{streamName}', will retry: {ex}",
                            }
                        );

                        retryDelay = _NextBackoff(retryDelay, floor: TimeSpan.FromSeconds(5));
                        await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                    catch (Exception ex)
                    {
                        OnLogCallback?.Invoke(
                            new LogMessageEventArgs
                            {
                                LogType = MqLogType.ExceptionReceived,
                                Reason = $"Consumer error for stream '{streamName}', will retry: {ex}",
                            }
                        );

                        retryDelay = _NextBackoff(retryDelay);
                        await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    if (msg is null)
                    {
                        continue;
                    }

                    retryDelay = TimeSpan.FromSeconds(1);
                    await _DispatchMessageAsync(msg, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                startupReady.TrySetCanceled(cancellationToken);
                break;
            }
            catch (Exception ex)
            {
                OnLogCallback?.Invoke(
                    new LogMessageEventArgs
                    {
                        LogType = MqLogType.ExceptionReceived,
                        Reason = $"Consumer error for stream '{streamName}', will retry: {ex}",
                    }
                );

                retryDelay = _NextBackoff(retryDelay);
                await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async ValueTask _DispatchMessageAsync(
        INatsJSMsg<ReadOnlyMemory<byte>> msg,
        CancellationToken cancellationToken
    )
    {
        if (_semaphore is not null)
        {
            await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

            _ObserveBackgroundHandler(
                Task.Run(
                    async () =>
                    {
                        try
                        {
                            await _ProcessMessageAsync(msg).ConfigureAwait(false);
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
            await _ProcessMessageAsync(msg).ConfigureAwait(false);
        }
    }

    private void _ObserveBackgroundHandler(Task task)
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

    internal static TimeSpan _NextBackoff(TimeSpan current, TimeSpan floor = default)
    {
        var ceiling = TimeSpan.FromSeconds(30);
        var next = TimeSpan.FromTicks(Math.Min(current.Ticks * 2, ceiling.Ticks));
        return floor > next ? floor : next;
    }

    private async Task _ProcessMessageAsync(INatsJSMsg<ReadOnlyMemory<byte>> msg)
    {
        TransportMessage message;
        try
        {
            var headers = new Dictionary<string, string?>(StringComparer.Ordinal);

            if (msg.Headers is { Count: > 0 } natsHeaders)
            {
                foreach (var (key, values) in natsHeaders)
                {
                    headers[key] = values.Count > 0 ? values[0] : null;
                }
            }

            headers[Headers.Group] = name;

            if (_natsOptions.CustomHeadersBuilder is not null)
            {
                var metadata = msg.Metadata;
                var customHeaders = _natsOptions.CustomHeadersBuilder(metadata, msg.Headers, serviceProvider);
                foreach (var customHeader in customHeaders)
                {
                    headers[customHeader.Key] = customHeader.Value;
                }
            }

            message = new TransportMessage(headers, msg.Data);
        }
        catch (Exception ex)
        {
            OnLogCallback?.Invoke(
                new LogMessageEventArgs
                {
                    LogType = MqLogType.ConsumeError,
                    Reason = $"Failed to build transport message, nacking: {ex}",
                }
            );

            await RejectAsync(msg).ConfigureAwait(false);
            return;
        }

        // Let exceptions propagate. The framework's OnMessageCallback handler
        // calls CommitAsync on success and RejectAsync on failure.
        await OnMessageCallback!(message, msg).ConfigureAwait(false);
    }

    public async ValueTask CommitAsync(object? sender)
    {
        try
        {
            if (sender is INatsJSMsg<ReadOnlyMemory<byte>> msg)
            {
                await msg.AckAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            OnLogCallback?.Invoke(
                new LogMessageEventArgs
                {
                    LogType = MqLogType.AsyncErrorEvent,
                    Reason = $"NATS message ACK failed: {ex}",
                }
            );
        }
    }

    public async ValueTask RejectAsync(object? sender)
    {
        try
        {
            if (sender is INatsJSMsg<ReadOnlyMemory<byte>> msg)
            {
                await msg.NakAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            OnLogCallback?.Invoke(
                new LogMessageEventArgs
                {
                    LogType = MqLogType.AsyncErrorEvent,
                    Reason = $"NATS message NAK failed: {ex}",
                }
            );
        }
    }

    private void _ReleaseSemaphore()
    {
        if (_semaphore is null)
        {
            return;
        }

        try
        {
            _semaphore.Release();
        }
        catch (SemaphoreFullException)
        {
            // Defensive: ignore over-release.
        }
        catch (ObjectDisposedException)
        {
            // Shutdown in progress. Semaphore already disposed.
        }
    }

    public async ValueTask PauseAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        if (!await _pauseGate.PauseAsync())
        {
            return;
        }

        _CancelReceives();
    }

    public async ValueTask ResumeAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        if (!await _pauseGate.ResumeAsync())
        {
            return;
        }

        _ResetReceiveToken();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _pauseGate.Release();
        _ready.TrySetCanceled();
        _CancelReceives();

        ReceiveTokenState? receiveTokenStateToDispose = null;
        lock (_receiveLock)
        {
            _receiveTokenState.Retired = true;
            if (_receiveTokenState.RefCount == 0)
            {
                receiveTokenStateToDispose = _receiveTokenState;
            }
        }

        receiveTokenStateToDispose?.Dispose();
        _semaphore?.Dispose();

        if (_connection is not null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    private ReceiveTokenLease _AcquireReceiveLease(CancellationToken cancellationToken)
    {
        ReceiveTokenState receiveTokenState;
        lock (_receiveLock)
        {
            receiveTokenState = _receiveTokenState;
            receiveTokenState.RefCount++;
        }

        try
        {
            return new ReceiveTokenLease(this, receiveTokenState, cancellationToken);
        }
        catch
        {
            _ReleaseReceiveTokenState(receiveTokenState);
            throw;
        }
    }

    private void _CancelReceives()
    {
        ReceiveTokenState receiveTokenState;
        lock (_receiveLock)
        {
            receiveTokenState = _receiveTokenState;
        }

        try
        {
            receiveTokenState.Source.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Shutdown already disposed the receive CTS.
        }
    }

    private void _ResetReceiveToken()
    {
        ReceiveTokenState? receiveTokenStateToDispose = null;
        lock (_receiveLock)
        {
            var previousState = _receiveTokenState;
            previousState.Retired = true;
            _receiveTokenState = new ReceiveTokenState();

            if (previousState.RefCount == 0)
            {
                receiveTokenStateToDispose = previousState;
            }
        }

        receiveTokenStateToDispose?.Dispose();
    }

    private void _ReleaseReceiveTokenState(ReceiveTokenState receiveTokenState)
    {
        bool shouldDispose;

        lock (_receiveLock)
        {
            receiveTokenState.RefCount--;
            shouldDispose = receiveTokenState.RefCount == 0 && receiveTokenState.Retired;
        }

        if (shouldDispose)
        {
            receiveTokenState.Dispose();
        }
    }

    private sealed class ReceiveTokenState : IDisposable
    {
        private readonly Lock _lock = new();
        private CancellationTokenSource? _linkedSource;
        private CancellationToken _lastParentToken;

        public CancellationTokenSource Source { get; } = new();

        public int RefCount { get; set; }

        public bool Retired { get; set; }

        public CancellationToken GetLinkedToken(CancellationToken parentToken)
        {
            if (parentToken == CancellationToken.None)
            {
                return Source.Token;
            }

            lock (_lock)
            {
                if (_linkedSource == null || _lastParentToken != parentToken)
                {
                    _linkedSource?.Dispose();
                    _linkedSource = CancellationTokenSource.CreateLinkedTokenSource(parentToken, Source.Token);
                    _lastParentToken = parentToken;
                }

                return _linkedSource.Token;
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _linkedSource?.Dispose();
                _linkedSource = null;
            }

            Source.Dispose();
        }
    }

    private sealed class ReceiveTokenLease(
        NatsConsumerClient owner,
        ReceiveTokenState receiveTokenState,
        CancellationToken cancellationToken
    ) : IDisposable
    {
#pragma warning disable CA2213 // _owner is a parent reference, not owned by this lease
        private readonly NatsConsumerClient _owner = owner;
#pragma warning restore CA2213
        private readonly ReceiveTokenState _receiveTokenState = receiveTokenState;
        private int _disposed;

        public CancellationToken Token { get; } = receiveTokenState.GetLinkedToken(cancellationToken);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _owner._ReleaseReceiveTokenState(_receiveTokenState);
        }
    }
}
