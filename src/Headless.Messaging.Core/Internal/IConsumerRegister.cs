// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics;
using Headless.Messaging.CircuitBreaker;
using Headless.Messaging.Configuration;
using Headless.Messaging.Diagnostics;
using Headless.Messaging.Exceptions;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.Messaging.Serialization;
using Headless.Messaging.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Internal;

/// <summary>
/// Handler received message of subscribed.
/// </summary>
public interface IConsumerRegister : IProcessingServer
{
    bool IsHealthy();

    ValueTask ReStartAsync(bool force = false);
}

internal sealed class ConsumerRegister(ILogger<ConsumerRegister> logger, IServiceProvider serviceProvider)
    : IConsumerRegister
{
    private static readonly DiagnosticListener _DiagnosticListener = new(
        MessageDiagnosticListenerNames.DiagnosticListenerName
    );

    private readonly ConcurrentDictionary<string, GroupHandle> _groupHandles = new(StringComparer.Ordinal);
    private readonly ILogger _logger = logger;
    private readonly MessagingOptions _options = serviceProvider.GetRequiredService<IOptions<MessagingOptions>>().Value;
    private readonly TimeSpan _pollingDelay = TimeSpan.FromSeconds(1);

    private ICircuitBreakerStateManager? _circuitBreakerStateManager;
    private IConsumerClientFactory _consumerClientFactory = null!;
    private IDispatcher _dispatcher = null!;
    private int _disposed;
    private volatile bool _isHealthy = true;

    private MethodMatcherCache _selector = null!;
    private ISerializer _serializer = null!;
    private BrokerAddress _serverAddress;
    private CancellationToken _hostStoppingToken;
    private CancellationTokenSource _stoppingCts = new();
    private CancellationTokenRegistration _stoppingCtsRegistration;
    private IDataStorage _storage = null!;

    public bool IsHealthy()
    {
        return _isHealthy;
    }

    public async ValueTask StartAsync(CancellationToken stoppingToken)
    {
        _hostStoppingToken = stoppingToken;
        _stoppingCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        _stoppingCtsRegistration = _stoppingCts.Token.Register(_OnCancellationRequested);

        _selector = serviceProvider.GetRequiredService<MethodMatcherCache>();
        _dispatcher = serviceProvider.GetRequiredService<IDispatcher>();
        _serializer = serviceProvider.GetRequiredService<ISerializer>();
        _storage = serviceProvider.GetRequiredService<IDataStorage>();
        _consumerClientFactory = serviceProvider.GetRequiredService<IConsumerClientFactory>();
        _circuitBreakerStateManager = serviceProvider.GetService<ICircuitBreakerStateManager>();

        await ExecuteAsync();

        _disposed = 0;
    }

    public async ValueTask ReStartAsync(bool force = false)
    {
        if (!IsHealthy() || force)
        {
            await PulseAsync();

            _stoppingCts = CancellationTokenSource.CreateLinkedTokenSource(_hostStoppingToken);
            _stoppingCtsRegistration = _stoppingCts.Token.Register(_OnCancellationRequested);
            _isHealthy = true;

            await ExecuteAsync();
        }
    }

    public void Dispose()
    {
        // Forward to DisposeAsync so synchronous callers still get real cleanup.
        _OnCancellationRequested();
    }

    /// <summary>
    /// Callback for <see cref="CancellationToken.Register(Action)"/>. Fires <see cref="DisposeAsync"/>
    /// on the thread-pool because the registration callback is synchronous and must not block.
    /// </summary>
    private void _OnCancellationRequested()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await DisposeAsync();
            }
#pragma warning disable ERP022 // Best-effort teardown — nothing useful to do with the exception
            catch { }
#pragma warning restore ERP022
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 1)
        {
            return;
        }

        try
        {
            await PulseAsync();
        }
        catch (AggregateException e)
        {
            var innerEx = e.InnerExceptions[0];
            if (innerEx is not OperationCanceledException)
            {
                _logger.ExpectedOperationCanceledException(innerEx, innerEx.Message);
            }
        }
        finally
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (_dispatcher is not null)
            {
                await _dispatcher.DisposeAsync();
            }
        }
    }

    public async Task PulseAsync()
    {
        // Cancel all group CTSes
        foreach (var handle in _groupHandles.Values)
        {
            await handle.Cts.CancelAsync();
        }

        // Wait for all consumer tasks to complete
        var allTasks = _groupHandles.Values.SelectMany(h => h.ConsumerTasks).ToArray();
        if (allTasks.Length > 0)
        {
            try
            {
                await Task.WhenAll(allTasks).WaitAsync(TimeSpan.FromSeconds(2));
            }
#pragma warning disable ERP022, RCS1075 // Intentional: timeout or cancellation — proceed with cleanup
            catch (Exception) { }
#pragma warning restore ERP022, RCS1075
        }

        // Dispose all handles
        foreach (var handle in _groupHandles.Values)
        {
            await handle.DisposeAsync();
            _circuitBreakerStateManager?.RemoveGroup(handle.GroupName);
        }

        _groupHandles.Clear();

        // Dispose the token registration before disposing the CTS to prevent
        // accumulated Dispose callbacks across successive ReStartAsync calls.
        // CTS.Dispose alone does not deregister Token.Register callbacks.
        await _stoppingCtsRegistration.DisposeAsync();
        _stoppingCts.Dispose();
    }

    public async ValueTask ExecuteAsync()
    {
        var groupingMatches = _selector.GetCandidatesMethodsOfGroupNameGrouped();

        // Arm the OTel cardinality guard so unrecognized group names are rejected.
        _circuitBreakerStateManager?.RegisterKnownGroups(groupingMatches.Keys);

        foreach (var matchGroup in groupingMatches)
        {
            var groupName = matchGroup.Key;
            var limit = _selector.GetGroupConcurrentLimit(groupName);

            ICollection<string> topics;
            try
            {
                await using var client = await _consumerClientFactory.CreateAsync(groupName, limit);
                client.OnLogCallback = _WriteLog;
                topics = await client.FetchTopicsAsync(matchGroup.Value.Select(x => x.TopicName));
            }
            catch (BrokerConnectionException e)
            {
                _isHealthy = false;
                _logger.LogError(e, "Failed to connect to broker. {Message}", e.Message);
                return;
            }

            var groupCts = CancellationTokenSource.CreateLinkedTokenSource(_stoppingCts.Token);
            var handle = new GroupHandle
            {
                Logger = _logger,
                Cts = groupCts,
                GroupName = groupName,
            };

            _groupHandles[groupName] = handle;
            _circuitBreakerStateManager?.RegisterGroupCallbacks(
                groupName,
                onPause: () => _PauseGroupAsync(handle),
                onResume: () => _ResumeGroupAsync(handle)
            );

            for (var i = 0; i < _options.ConsumerThreadCount; i++)
            {
                var task = Task
                    .Factory.StartNew(
                        async () =>
                        {
                            try
                            {
                                var innerClient = await _consumerClientFactory.CreateAsync(groupName, limit);

                                handle.AddClient(innerClient);

                                _serverAddress = innerClient.BrokerAddress;

                                _RegisterMessageProcessor(innerClient);

                                await innerClient.SubscribeAsync(topics);

                                await innerClient.ListeningAsync(_pollingDelay, groupCts.Token);
                            }
                            catch (OperationCanceledException)
                            {
                                // ignore
                            }
                            catch (BrokerConnectionException e)
                            {
                                _isHealthy = false;
                                _logger.LogError(e, "Failed to connect to broker. {Message}", e.Message);
                            }
                            catch (Exception e)
                            {
                                _logger.LogError(
                                    e,
                                    "An exception occurred in consumer processing loop. {Message}",
                                    e.Message
                                );
                            }
                        },
                        groupCts.Token,
                        TaskCreationOptions.LongRunning,
                        TaskScheduler.Default
                    )
                    .Unwrap();

                handle.ConsumerTasks.Add(task);
            }
        }
    }

    private async ValueTask _PauseGroupAsync(GroupHandle handle)
    {
        _logger.LogWarning("Circuit breaker opened for group '{GroupName}'. Pausing consumers.", handle.GroupName);

        // Do NOT cancel the CTS here — the ListeningAsync loops must stay alive so they can
        // resume without restarting tasks. Transport-level pause (PauseAsync) is sufficient:
        // MRES-based transports block at the pause gate, RabbitMQ cancels the consumer,
        // and Kafka pauses partition polling.
        handle.IsPaused = true;
        var snapshot = handle.SnapshotClients();

        foreach (var client in snapshot)
        {
            try
            {
                await client.PauseAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to pause consumer client for group '{GroupName}'.", handle.GroupName);
            }
        }
    }

    private async ValueTask _ResumeGroupAsync(GroupHandle handle)
    {
        _logger.LogDebug(
            "Resuming consumers for group '{GroupName}' (half-open).",
            handle.GroupName
        );

        // No CTS recreation needed — the original CTS was never cancelled during pause,
        // so ListeningAsync loops are still running. Just un-gate the transport.
        handle.IsPaused = false;
        var snapshot = handle.SnapshotClients();

        foreach (var client in snapshot)
        {
            try
            {
                await client.ResumeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to resume consumer client for group '{GroupName}'.", handle.GroupName);
            }
        }
    }

    private void _RegisterMessageProcessor(IConsumerClient client)
    {
        client.OnLogCallback = _WriteLog;
        client.OnMessageCallback = async (transportMessage, sender) =>
        {
            // Fast path: skip sanitization for groups registered at startup (trusted config).
            var rawGroup = transportMessage.GetGroup();
            var groupName = rawGroup is not null && _groupHandles.ContainsKey(rawGroup)
                ? rawGroup
                : _SanitizeGroupName(rawGroup);
            var probeAcquired = false;
            var probeOutcomeTransferred = false;
            long? tracingTimestamp = null;
            try
            {
                if (groupName is not null && _circuitBreakerStateManager is not null)
                {
                    probeAcquired = _circuitBreakerStateManager.TryAcquireHalfOpenProbe(groupName);

                    if (!probeAcquired)
                    {
                        await client.RejectAsync(sender);
                        return;
                    }
                }

                _logger.MessageReceived(
                    _SanitizeHeader(transportMessage.GetId()) ?? "(null)",
                    _SanitizeHeader(transportMessage.GetName()) ?? "(null)"
                );

                tracingTimestamp = _TracingBefore(transportMessage, _serverAddress);

                var name = transportMessage.GetName();
                var group = groupName!;

                Message message;
                Exception? dispatchBypassException = null;

                var canFindSubscriber = _selector.TryGetTopicExecutor(name, group, out var executor);
                string? exceptionInfo = null;
                try
                {
                    if (!canFindSubscriber)
                    {
                        var error =
                            $"Message can not be found subscriber. Name:{name}, Group:{group}. {Environment.NewLine} Ensure the subscriber method is decorated with [Subscribe] and the consumer group matches.";
                        var ex = new SubscriberNotFoundException(error);

                        _TracingError(tracingTimestamp, transportMessage, client.BrokerAddress, ex);

                        throw ex;
                    }

                    // Extract the actual message type for deserialization
                    // For IConsume<T>.Consume(ConsumeContext<T>, CancellationToken), we need T, not ConsumeContext<T>
                    var paramType = executor!.Parameters.FirstOrDefault(x => !x.IsFromMessaging)?.ParameterType;
                    var messageValueType =
                        paramType is { IsGenericType: true }
                        && paramType.GetGenericTypeDefinition() == typeof(ConsumeContext<>)
                            ? paramType.GetGenericArguments()[0]
                            : paramType;

                    message = await _serializer.DeserializeAsync(transportMessage, messageValueType);
                    message.RemoveException();
                }
                catch (Exception e)
                {
                    dispatchBypassException = e;
#pragma warning disable EPC12 // Suppress CA2200 warning to rethrow original exception
                    transportMessage.Headers[Headers.Exception] = e.GetType().Name;
#pragma warning restore EPC12
                    exceptionInfo = e.ExpandMessage();

                    string? dataUri;
                    if (transportMessage.Headers.TryGetValue(Headers.Type, out var val))
                    {
                        dataUri =
                            transportMessage.Body.Length != 0
                                ? $"data:{val};base64," + Convert.ToBase64String(transportMessage.Body.Span)
                                : null;

                        message = new Message(transportMessage.Headers, dataUri);
                    }
                    else
                    {
                        dataUri =
                            transportMessage.Body.Length != 0
                                ? "data:UnknownType;base64," + Convert.ToBase64String(transportMessage.Body.Span)
                                : null;

                        message = new Message(transportMessage.Headers, dataUri);
                    }
                }

                if (message.HasException())
                {
                    if (dispatchBypassException is not null && _circuitBreakerStateManager is not null)
                    {
                        await _circuitBreakerStateManager.ReportFailureAsync(group, dispatchBypassException)
                            .ConfigureAwait(false);
                        probeOutcomeTransferred = true;
                    }

#pragma warning disable CA1849, VSTHRD103
                    var content = _serializer.Serialize(message);
#pragma warning restore VSTHRD103, CA1849

                    await _storage.StoreReceivedExceptionMessageAsync(name, group, content, exceptionInfo);

                    await client.CommitAsync(sender);

                    try
                    {
                        _options.FailedThresholdCallback?.Invoke(
                            new FailedInfo
                            {
                                ServiceProvider = serviceProvider,
                                MessageType = MessageType.Subscribe,
                                Message = message,
                            }
                        );

                        _logger.ConsumerExecutedAfterThreshold(message.GetId(), _options.FailedRetryCount);
                    }
                    catch (Exception e)
                    {
                        _logger.ExecutedThresholdCallbackFailed(e, e.Message);
                    }

                    _TracingAfter(tracingTimestamp, transportMessage, _serverAddress);
                }
                else
                {
                    var mediumMessage = await _storage.StoreReceivedMessageAsync(name, group, message);
                    mediumMessage.Origin = message;

                    _TracingAfter(tracingTimestamp, transportMessage, _serverAddress);

                    await _dispatcher.EnqueueToExecute(mediumMessage, executor!);
                    probeOutcomeTransferred = true;

                    await client.CommitAsync(sender);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(
                    e,
                    "An exception occurred when process received message. Message:'{Message}'.",
                    transportMessage
                );

                await client.RejectAsync(sender);

                _TracingError(tracingTimestamp, transportMessage, client.BrokerAddress, e);
            }
            finally
            {
                if (probeAcquired && !probeOutcomeTransferred && groupName is not null)
                {
                    _circuitBreakerStateManager?.ReleaseHalfOpenProbe(groupName);
                }
            }
        };
    }

    /// <summary>
    /// Sanitizes a group name from a transport header to prevent log injection and
    /// unbounded memory growth. Strips control characters and truncates to 256 chars.
    /// Returns the original string unchanged when no sanitization is needed (hot-path friendly).
    /// </summary>
    private static string? _SanitizeGroupName(string? groupName)
    {
        if (groupName is null)
        {
            return null;
        }

        const int maxLength = 256;
        const string truncationSuffix = "...";

        var needsTruncation = groupName.Length > maxLength;
        var hasControlChars = false;

        var scanLength = needsTruncation ? maxLength : groupName.Length;
        for (var i = 0; i < scanLength; i++)
        {
            if (char.IsControl(groupName[i]))
            {
                hasControlChars = true;
                break;
            }
        }

        if (!needsTruncation && !hasControlChars)
        {
            return groupName;
        }

        var span = groupName.AsSpan(0, scanLength);

        if (!hasControlChars)
        {
            return needsTruncation
                ? string.Concat(span[..(maxLength - truncationSuffix.Length)], truncationSuffix)
                : groupName;
        }

        // Control chars present — build a clean string
        var effectiveMax = needsTruncation ? maxLength - truncationSuffix.Length : scanLength;
        var buffer = new char[effectiveMax + (needsTruncation ? truncationSuffix.Length : 0)];
        var pos = 0;

        for (var i = 0; i < scanLength && pos < effectiveMax; i++)
        {
            var c = span[i];
            if (!char.IsControl(c))
            {
                buffer[pos++] = c;
            }
        }

        if (needsTruncation)
        {
            truncationSuffix.AsSpan().CopyTo(buffer.AsSpan(pos));
            pos += truncationSuffix.Length;
        }

        return new string(buffer, 0, pos);
    }

    /// <summary>
    /// Sanitizes a broker message header value to prevent log injection.
    /// Strips control characters and Unicode bidi overrides (U+202A–U+202E, U+2066–U+2069).
    /// Returns null if input is null.
    /// </summary>
    private static string? _SanitizeHeader(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var needsSanitization = false;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsControl(c) || c is (>= '\u202A' and <= '\u202E') or (>= '\u2066' and <= '\u2069'))
            {
                needsSanitization = true;
                break;
            }
        }

        if (!needsSanitization)
        {
            return value;
        }

        var buffer = new char[value.Length];
        var pos = 0;

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (!char.IsControl(c) && c is not ((>= '\u202A' and <= '\u202E') or (>= '\u2066' and <= '\u2069')))
            {
                buffer[pos++] = c;
            }
        }

        return new string(buffer, 0, pos);
    }

    private void _WriteLog(LogMessageEventArgs logMessage)
    {
        var reason = _SanitizeHeader(logMessage.Reason) ?? string.Empty;

        switch (logMessage.LogType)
        {
            case MqLogType.ConsumerCancelled:
                _isHealthy = false;
                _logger.LogWarning("RabbitMQ consumer cancelled. --> {Reason}", reason);
                break;
            case MqLogType.ConsumerRegistered:
                _isHealthy = true;
                _logger.LogInformation("RabbitMQ consumer registered. --> {Reason}", reason);
                break;
            case MqLogType.ConsumerUnregistered:
                _logger.LogWarning("RabbitMQ consumer unregistered. --> {Reason}", reason);
                break;
            case MqLogType.ConsumerShutdown:
                _isHealthy = false;
                _logger.LogWarning("RabbitMQ consumer shutdown. --> {Reason}", reason);
                break;
            case MqLogType.ConsumeError:
                _logger.LogError("Kafka client consume error. --> {Reason}", reason);
                break;
            case MqLogType.ConsumeRetries:
                _logger.LogWarning("Kafka client consume exception, retying... --> {Reason}", reason);
                break;
            case MqLogType.ServerConnError:
                _isHealthy = false;
                _logger.LogCritical("Kafka server connection error. --> {Reason}", reason);
                break;
            case MqLogType.ExceptionReceived:
                _logger.LogError("AzureServiceBus subscriber received an error. --> {Reason}", reason);
                break;
            case MqLogType.AsyncErrorEvent:
                _logger.LogError("NATS subscriber received an error. --> {Reason}", reason);
                break;
            case MqLogType.ConnectError:
                _isHealthy = false;
                _logger.LogError("NATS server connection error. --> {Reason}", reason);
                break;
            case MqLogType.InvalidIdFormat:
                _logger.LogError(
                    "AmazonSQS subscriber delete inflight message failed, invalid id. --> {Reason}",
                    reason
                );
                break;
            case MqLogType.MessageNotInflight:
                _logger.LogError(
                    "AmazonSQS subscriber change message's visibility failed, message isn't in flight. --> {Reason}",
                    reason
                );
                break;
            case MqLogType.RedisConsumeError:
                _isHealthy = true;
                _logger.LogError("Redis client consume error. --> {Reason}", reason);
                break;
            default:
                throw new InvalidOperationException($"Unknown {nameof(MqLogType)}={logMessage.LogType}");
        }
    }

    private sealed class GroupHandle : IAsyncDisposable
    {
        private readonly Lock _clientsLock = new();
        private bool _disposing;
        private bool _isPaused;

        private readonly List<IConsumerClient> _clients = [];

        public required ILogger Logger { get; init; }
        public required CancellationTokenSource Cts { get; init; }
        public required string GroupName { get; init; }
        public List<Task> ConsumerTasks { get; init; } = [];

        public bool IsPaused
        {
            get { lock (_clientsLock) { return _isPaused; } }
            set { lock (_clientsLock) { _isPaused = value; } }
        }

        public void AddClient(IConsumerClient client)
        {
            bool shouldPause;
            lock (_clientsLock)
            {
                if (_disposing)
                {
                    // Already shutting down — dispose inline instead of adding.
                    // Fire-and-forget is acceptable here: we're inside a lock (can't await)
                    // and the group is already being torn down.
#pragma warning disable CA2012 // Use ValueTasks correctly
                    _ = client.DisposeAsync();
#pragma warning restore CA2012
                    return;
                }

                _clients.Add(client);
                shouldPause = _isPaused;
            }

            if (shouldPause)
            {
                // Fire-and-forget — the client will block at the pause gate on next poll
                // regardless; this just ensures immediate consistency.
                _ = client.PauseAsync().AsTask().ContinueWith(
                    t => Logger.LogError(
                        t.Exception,
                        "Failed to pause newly added consumer client for group '{GroupName}'.",
                        GroupName),
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted,
                    TaskScheduler.Default);
            }
        }

        public IConsumerClient[] SnapshotClients()
        {
            lock (_clientsLock)
            {
                return [.. _clients];
            }
        }

        public async ValueTask DisposeAsync()
        {
            await Cts.CancelAsync();
            Cts.Dispose();

            IConsumerClient[] snapshot;
            lock (_clientsLock)
            {
                _disposing = true;
                snapshot = [.. _clients];
            }

            foreach (var client in snapshot)
            {
                await client.DisposeAsync();
            }
        }
    }

    #region Tracing

    private static long? _TracingBefore(TransportMessage message, BrokerAddress broker)
    {
        if (_DiagnosticListener.IsEnabled(MessageDiagnosticListenerNames.BeforeConsume))
        {
            var eventData = new MessageEventDataSubStore
            {
                OperationTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Operation = message.GetName(),
                BrokerAddress = broker,
                TransportMessage = message,
            };

            _DiagnosticListener.Write(MessageDiagnosticListenerNames.BeforeConsume, eventData);

            return eventData.OperationTimestamp;
        }

        return null;
    }

    private static void _TracingAfter(long? tracingTimestamp, TransportMessage message, BrokerAddress broker)
    {
        MessageEventCounterSource.Log.WriteConsumeMetrics();
        if (tracingTimestamp != null && _DiagnosticListener.IsEnabled(MessageDiagnosticListenerNames.AfterConsume))
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var eventData = new MessageEventDataSubStore
            {
                OperationTimestamp = now,
                Operation = message.GetName(),
                BrokerAddress = broker,
                TransportMessage = message,
                ElapsedTimeMs = now - tracingTimestamp.Value,
            };

            _DiagnosticListener.Write(MessageDiagnosticListenerNames.AfterConsume, eventData);
        }
    }

    private static void _TracingError(
        long? tracingTimestamp,
        TransportMessage message,
        BrokerAddress broker,
        Exception ex
    )
    {
        if (tracingTimestamp != null && _DiagnosticListener.IsEnabled(MessageDiagnosticListenerNames.ErrorConsume))
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var eventData = new MessageEventDataSubStore
            {
                OperationTimestamp = now,
                Operation = message.GetName(),
                BrokerAddress = broker,
                TransportMessage = message,
                ElapsedTimeMs = now - tracingTimestamp.Value,
                Exception = ex,
            };

            _DiagnosticListener.Write(MessageDiagnosticListenerNames.ErrorConsume, eventData);
        }
    }

    #endregion
}
