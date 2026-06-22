// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics;
using Headless.Messaging.CircuitBreaker;
using Headless.Messaging.Configuration;
using Headless.Messaging.Diagnostics;
using Headless.Messaging.Exceptions;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.Messaging.Retry;
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

    ValueTask ReStartAsync(bool force = false, CancellationToken cancellationToken = default);
    ValueTask OnTopologyChangedAsync(CancellationToken cancellationToken = default);
}

internal sealed class ConsumerRegister(
    ILogger<ConsumerRegister> logger,
    IServiceProvider serviceProvider,
    IServiceScopeFactory serviceScopeFactory
) : IConsumerRegister
{
    private static readonly DiagnosticListener _DiagnosticListener = new(
        MessageDiagnosticListenerNames.DiagnosticListenerName
    );

    private readonly ConcurrentDictionary<string, GroupHandle> _groupHandles = new(StringComparer.Ordinal);
    private readonly ILogger _logger = logger;
    private readonly MessagingOptions _options = serviceProvider.GetRequiredService<IOptions<MessagingOptions>>().Value;
    private readonly TimeProvider _timeProvider = serviceProvider.GetRequiredService<TimeProvider>();
    private readonly TimeSpan _pollingDelay = TimeSpan.FromSeconds(1);

    private ICircuitBreakerStateManager? _circuitBreakerStateManager;
    private IConsumerClientFactory _consumerClientFactory = null!;
    private IDispatcher _dispatcher = null!;
    private int _state = (int)LifecycleState.NotStarted;
    private readonly SemaphoreSlim _restartGate = new(1, 1);
    private volatile bool _isHealthy = true;
    private int _pendingTopologyRefresh;

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
        Interlocked.Exchange(ref _state, (int)LifecycleState.Starting);
        Interlocked.Exchange(ref _pendingTopologyRefresh, 0);

        _selector = serviceProvider.GetRequiredService<MethodMatcherCache>();
        _dispatcher = serviceProvider.GetRequiredService<IDispatcher>();
        _serializer = serviceProvider.GetRequiredService<ISerializer>();
        _storage = serviceProvider.GetRequiredService<IDataStorage>();
        _consumerClientFactory = serviceProvider.GetRequiredService<IConsumerClientFactory>();
        _circuitBreakerStateManager = serviceProvider.GetService<ICircuitBreakerStateManager>();

        try
        {
            await ExecuteAsync().ConfigureAwait(false);

            // Acquire the restart gate so topology-change-driven restarts cannot overlap
            // with the drain loop that follows the initial startup.
            await _restartGate.WaitAsync(stoppingToken).ConfigureAwait(false);
            try
            {
                Interlocked.Exchange(ref _state, (int)LifecycleState.Running);
                await _DrainPendingTopologyRefreshesAsync().ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    _restartGate.Release();
                }
                catch (ObjectDisposedException)
                {
                    // If we failed to acquire the gate above, it means DisposeAsync has already run, and we should not attempt to release.
                }
            }
        }
        catch
        {
            // Clean up any partially created consumer handles before resetting state.
            try
            {
                await PulseAsync().ConfigureAwait(false);
            }
#pragma warning disable ERP022 // Best-effort cleanup — state reset below prevents stale handles from being accessible.
            // ReSharper disable once EmptyGeneralCatchClause
            catch { }
#pragma warning restore ERP022

            Interlocked.Exchange(ref _state, (int)LifecycleState.NotStarted);
            Interlocked.Exchange(ref _pendingTopologyRefresh, 0);
            throw;
        }
    }

    public async ValueTask ReStartAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        if (!IsHealthy() || force)
        {
            await _restartGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _RestartCoreAsync().ConfigureAwait(false);
                await _DrainPendingTopologyRefreshesAsync().ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    _restartGate.Release();
                }
                catch (ObjectDisposedException) { }
            }
        }
    }

    public async ValueTask OnTopologyChangedAsync(CancellationToken cancellationToken = default)
    {
        var current = (LifecycleState)Volatile.Read(ref _state);

        if (current == LifecycleState.Running)
        {
            await ReStartAsync(force: true, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (current == LifecycleState.Starting)
        {
            Interlocked.Exchange(ref _pendingTopologyRefresh, 1);
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
        _ = Task.Run(
            async () =>
            {
                try
                {
                    await DisposeAsync().ConfigureAwait(false);
                }
#pragma warning disable ERP022 // Best-effort teardown — nothing useful to do with the exception
                // ReSharper disable once EmptyGeneralCatchClause
                catch { }
#pragma warning restore ERP022
            },
            CancellationToken.None
        );
    }

    public async ValueTask DisposeAsync()
    {
        // Spin until we win the transition to Disposed, or discover someone else already did.
        while (true)
        {
            var current = (LifecycleState)Volatile.Read(ref _state);

            if (current is LifecycleState.Disposing or LifecycleState.Disposed)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _state, (int)LifecycleState.Disposing, (int)current) == (int)current)
            {
                break;
            }
        }

        try
        {
            await PulseAsync().ConfigureAwait(false);
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
                await _dispatcher.DisposeAsync().ConfigureAwait(false);
            }

            _restartGate.Dispose();
            Interlocked.Exchange(ref _state, (int)LifecycleState.Disposed);
        }
    }

    public async Task PulseAsync(bool removeCircuitState = true)
    {
        // Cancel all group CTSes
        foreach (var handle in _groupHandles.Values)
        {
            await handle.Cts.CancelAsync().ConfigureAwait(false);
        }

        // Wait for all consumer tasks to complete
        var allTasks = _groupHandles.Values.SelectMany(h => h.ConsumerTasks).ToArray();
        if (allTasks.Length > 0)
        {
            try
            {
                await Task.WhenAll(allTasks)
                    .WaitAsync(TimeSpan.FromSeconds(2), _timeProvider, CancellationToken.None)
                    .ConfigureAwait(false);
            }
#pragma warning disable ERP022, RCS1075 // Intentional: timeout or cancellation — proceed with cleanup
            // ReSharper disable once EmptyGeneralCatchClause
            catch (Exception) { }
#pragma warning restore ERP022, RCS1075
        }

        // Dispose all handles; only remove circuit state on final teardown,
        // not on transport restarts where state must survive broker reconnects.
        foreach (var handle in _groupHandles.Values)
        {
            await handle.DisposeAsync().ConfigureAwait(false);
            if (removeCircuitState)
            {
                if (_circuitBreakerStateManager is not null)
                {
                    await _circuitBreakerStateManager.RemoveGroupAsync(handle.GroupName).ConfigureAwait(false);
                }
            }
        }

        _groupHandles.Clear();

        // Dispose the token registration before disposing the CTS to prevent
        // accumulated Dispose callbacks across successive ReStartAsync calls.
        // CTS.Dispose alone does not deregister Token.Register callbacks.
        await _stoppingCtsRegistration.DisposeAsync().ConfigureAwait(false);
        _stoppingCts.Dispose();
    }

    public async ValueTask ExecuteAsync()
    {
        var groupingMatches = _selector.GetCandidatesMethodsOfIntentGroupNameGrouped();
        List<Task>? startupTasks = null;

        _EnsureConsumerFactorySupportsIntent(groupingMatches.Keys);

        // Arm the OTel cardinality guard so unrecognized group names are rejected.
        _circuitBreakerStateManager?.RegisterKnownGroups(groupingMatches.Keys.Select(_CreateHandleName));

        foreach (var matchGroup in groupingMatches)
        {
            var groupKey = matchGroup.Key;
            var groupName = groupKey.GroupName;
            var intentType = groupKey.IntentType;
            var handleName = _CreateHandleName(groupKey);
            var limit = _selector.GetGroupConcurrentLimit(groupKey);

            ICollection<string> messageNames;
            try
            {
                await using var client = await _CreateConsumerClientAsync(groupName, limit, intentType)
                    .ConfigureAwait(false);
                client.OnLogCallback = _WriteLog;
                messageNames = await client
                    .FetchMessageNamesAsync(matchGroup.Value.Select(x => x.MessageName))
                    .ConfigureAwait(false);
            }
            catch (BrokerConnectionException e)
            {
                _isHealthy = false;
                _logger.FailedToConnectToBroker(e);
                return;
            }

            var groupCts = CancellationTokenSource.CreateLinkedTokenSource(_stoppingCts.Token);
            var handle = new GroupHandle
            {
                Logger = _logger,
                Cts = groupCts,
                GroupName = handleName,
            };

            _groupHandles[handleName] = handle;
            _circuitBreakerStateManager?.RegisterGroupCallbacks(
                handleName,
                onPause: () => _PauseGroupAsync(handle),
                onResume: () => _ResumeGroupAsync(handle)
            );

            // Normalize HalfOpen → Open: the aborted probe is invalid on rebuilt transport clients.
            // This is a no-op during initial startup (no groups are in HalfOpen then).
            if (_circuitBreakerStateManager is not null)
            {
                await _circuitBreakerStateManager.AbortHalfOpenProbeAsync(handleName).ConfigureAwait(false);

                // If the circuit is Open (or was just re-normalized from HalfOpen),
                // pre-pause the new handle so newly created clients get paused via AddClientAsync.
                if (_circuitBreakerStateManager.IsOpen(handleName))
                {
                    handle.IsPaused = true;
                }
            }

            for (var i = 0; i < _options.ConsumerThreadCount; i++)
            {
                var startupReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var task = Task
                    .Factory.StartNew(
                        async () =>
                        {
                            try
                            {
                                var innerClient = await _CreateConsumerClientAsync(groupName, limit, intentType)
                                    .ConfigureAwait(false);

                                await handle.AddClientAsync(innerClient).ConfigureAwait(false);

                                _serverAddress = innerClient.BrokerAddress;

                                _RegisterMessageProcessor(
                                    innerClient,
                                    groupName,
                                    handleName,
                                    intentType,
                                    groupCts.Token
                                );

                                await innerClient.SubscribeAsync(messageNames).ConfigureAwait(false);
                                await _AwaitConsumerReadyThenListenAsync(innerClient, startupReady, groupCts.Token)
                                    .ConfigureAwait(false);
                            }
                            catch (OperationCanceledException)
                            {
                                startupReady.TrySetCanceled(groupCts.Token);
                            }
                            catch (BrokerConnectionException e)
                            {
                                startupReady.TrySetException(e);
                                _isHealthy = false;
                                _logger.FailedToConnectToBroker(e);
                            }
                            catch (Exception e)
                            {
                                startupReady.TrySetException(e);
                                _logger.ConsumerProcessingLoopFailed(e);
                            }
                        },
                        groupCts.Token,
                        TaskCreationOptions.LongRunning,
                        TaskScheduler.Default
                    )
                    .Unwrap();

                handle.ConsumerTasks.Add(task);
                startupTasks ??= [];
                startupTasks.Add(startupReady.Task);
            }
        }

        if (startupTasks is { Count: > 0 })
        {
            await Task.WhenAll(startupTasks).ConfigureAwait(false);
        }
    }

    private Task<IConsumerClient> _CreateConsumerClientAsync(
        string groupName,
        byte groupConcurrent,
        IntentType intentType
    )
    {
        return _consumerClientFactory is IIntentAwareConsumerClientFactory intentAwareFactory
            ? intentAwareFactory.CreateAsync(groupName, groupConcurrent, intentType)
            : _consumerClientFactory.CreateAsync(groupName, groupConcurrent);
    }

    private void _EnsureConsumerFactorySupportsIntent(IEnumerable<ConsumerGroupKey> groupKeys)
    {
        if (_consumerClientFactory is IIntentAwareConsumerClientFactory)
        {
            return;
        }

        var unsupportedGroup = groupKeys.FirstOrDefault(group => group.IntentType != IntentType.Bus);

        if (unsupportedGroup != default)
        {
            throw new InvalidOperationException(
                $"Consumer group '{unsupportedGroup.GroupName}' is registered for {unsupportedGroup.IntentType} delivery, "
                    + $"but the configured {nameof(IConsumerClientFactory)} does not implement "
                    + $"{nameof(IIntentAwareConsumerClientFactory)}. Use an intent-aware messaging provider for queue consumers."
            );
        }
    }

    private static string _CreateHandleName(ConsumerGroupKey groupKey) =>
        CircuitBreakerGroupKeys.For(groupKey.IntentType, groupKey.GroupName);

    private async Task _AwaitConsumerReadyThenListenAsync(
        IConsumerClient innerClient,
        TaskCompletionSource startupReady,
        CancellationToken cancellationToken
    )
    {
        var readinessTask = innerClient.WaitUntilReadyAsync(cancellationToken).AsTask();

        if (readinessTask.IsCompleted)
        {
            await readinessTask.ConfigureAwait(false);
            startupReady.TrySetResult();
            await innerClient.ListeningAsync(_pollingDelay, cancellationToken).ConfigureAwait(false);
            return;
        }

        var listeningTask = Task.Run(
            () => innerClient.ListeningAsync(_pollingDelay, cancellationToken).AsTask(),
            CancellationToken.None
        );

        var completedTask = await Task.WhenAny(readinessTask, listeningTask).ConfigureAwait(false);
        if (completedTask == listeningTask)
        {
            await listeningTask.ConfigureAwait(false);
        }

        await readinessTask.ConfigureAwait(false);
        startupReady.TrySetResult();
        await listeningTask.ConfigureAwait(false);
    }

    private async ValueTask _RestartCoreAsync()
    {
        var current = (LifecycleState)Volatile.Read(ref _state);
        if (current is LifecycleState.Disposing or LifecycleState.Disposed)
        {
            return;
        }

        Interlocked.Exchange(ref _state, (int)LifecycleState.Starting);
        Interlocked.Exchange(ref _pendingTopologyRefresh, 0);

        // Preserve circuit breaker state across transport restarts — broker reconnects
        // are orthogonal to handler failures tracked by the circuit breaker.
        await PulseAsync(removeCircuitState: false).ConfigureAwait(false);

        _stoppingCts = CancellationTokenSource.CreateLinkedTokenSource(_hostStoppingToken);
        _stoppingCtsRegistration = _stoppingCts.Token.Register(_OnCancellationRequested);
        _isHealthy = true;

        try
        {
            await ExecuteAsync().ConfigureAwait(false);
        }
        catch
        {
            // Clean up any partially created consumer handles and the CTS created above.
            try
            {
                await PulseAsync().ConfigureAwait(false);
            }
#pragma warning disable ERP022 // Best-effort cleanup — state reset below prevents stale handles from being accessible.
            catch
            {
                // ignore
            }
#pragma warning restore ERP022

            Interlocked.Exchange(ref _state, (int)LifecycleState.NotStarted);
            Interlocked.Exchange(ref _pendingTopologyRefresh, 0);
            throw;
        }

        Interlocked.Exchange(ref _state, (int)LifecycleState.Running);
    }

    private async ValueTask _DrainPendingTopologyRefreshesAsync()
    {
        while (Interlocked.Exchange(ref _pendingTopologyRefresh, 0) == 1)
        {
            await _RestartCoreAsync().ConfigureAwait(false);
        }
    }

    private async ValueTask _PauseGroupAsync(GroupHandle handle)
    {
        _logger.CircuitBreakerOpenedPausingConsumers(handle.GroupName);

        // Do NOT cancel the CTS here — the ListeningAsync loops must stay alive so they can
        // resume without restarting tasks. Transport-level pause (PauseAsync) is sufficient:
        // MRES-based transports block at the pause gate, RabbitMQ cancels the consumer,
        // and Kafka pauses partition polling.
        handle.IsPaused = true;
        var snapshot = handle.SnapshotClients();

        await Task.WhenAll(
                snapshot.Select(async client =>
                {
                    try
                    {
                        await client.PauseAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.PauseConsumerClientFailed(ex, handle.GroupName);
                    }
                })
            )
            .ConfigureAwait(false);
    }

    private async ValueTask _ResumeGroupAsync(GroupHandle handle)
    {
        _logger.ResumingConsumersHalfOpen(handle.GroupName);

        // No CTS recreation needed — the original CTS was never cancelled during pause,
        // so ListeningAsync loops are still running. Just un-gate the transport.
        handle.IsPaused = false;
        var snapshot = handle.SnapshotClients();
        ConcurrentBag<Exception> failures = [];

        await Task.WhenAll(
                snapshot.Select(async client =>
                {
                    try
                    {
                        await client.ResumeAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.ResumeConsumerClientFailed(ex, handle.GroupName);
                        failures.Add(ex);
                    }
                })
            )
            .ConfigureAwait(false);

        if (failures.IsEmpty)
        {
            return;
        }

        var failureList = failures.ToArray();

        if (failureList.Length == 1)
        {
            throw failureList[0];
        }

        throw new AggregateException(
            $"Failed to resume one or more consumer clients for group '{handle.GroupName}'.",
            failureList
        );
    }

    private void _RegisterMessageProcessor(
        IConsumerClient client,
        string group,
        string handleName,
        IntentType intentType,
        CancellationToken hostShutdownToken
    )
    {
        client.OnLogCallback = _WriteLog;
        client.OnMessageCallback = async (transportMessage, sender) =>
        {
            var probeAcquired = false;
            var probeOutcomeTransferred = false;
            long? tracingTimestamp = null;
            try
            {
                if (_circuitBreakerStateManager is not null)
                {
                    probeAcquired = _circuitBreakerStateManager.TryAcquireHalfOpenProbe(handleName);

                    if (!probeAcquired)
                    {
                        await client.RejectAsync(sender).ConfigureAwait(false);
                        return;
                    }
                }

                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    var safeMessageId = LogSanitizer.Sanitize(transportMessage.GetId());
                    var safeMessageName = LogSanitizer.Sanitize(transportMessage.GetName());
                    _logger.MessageReceived(safeMessageId, safeMessageName);
                }

                tracingTimestamp = _TracingBefore(transportMessage, intentType, _serverAddress, hostShutdownToken);

                var name = transportMessage.GetName();

                Message message;
                Exception? dispatchBypassException = null;

                var canFindSubscriber = _selector.TryGetMessageNameExecutor(name, group, intentType, out var executor);
                string? exceptionInfo = null;
                try
                {
                    if (!canFindSubscriber)
                    {
                        var safeName = LogSanitizer.Sanitize(name);
                        var safeGroup = LogSanitizer.Sanitize(group);
                        var error =
                            $"Message can not be found subscriber. Name:{safeName}, Group:{safeGroup}. {Environment.NewLine} Ensure the subscriber method is decorated with [Subscribe] and the consumer group matches.";
                        var ex = new SubscriberNotFoundException(error);

                        _TracingError(
                            tracingTimestamp,
                            transportMessage,
                            intentType,
                            client.BrokerAddress,
                            ex,
                            hostShutdownToken
                        );

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

                    message = await _serializer
                        .DeserializeAsync(transportMessage, messageValueType)
                        .ConfigureAwait(false);
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
                        await _circuitBreakerStateManager
                            .ReportFailureAsync(handleName, dispatchBypassException, CancellationToken.None)
                            .ConfigureAwait(false);
                        probeOutcomeTransferred = true;
                    }

#pragma warning disable CA1849, VSTHRD103
                    var content = _serializer.Serialize(message);
#pragma warning restore VSTHRD103, CA1849

                    var stored = await _storage
                        .StoreReceivedExceptionMessageAsync(
                            name,
                            group,
                            new MediumMessage
                            {
                                StorageId = Guid.Empty,
                                Origin = message,
                                Content = content,
                                IntentType = intentType,
                            },
                            exceptionInfo,
                            hostShutdownToken
                        )
                        .ConfigureAwait(false);

                    await client.CommitAsync(sender).ConfigureAwait(false);

                    var bypassCallback = _options.RetryPolicy.OnExhausted;
                    if (stored && bypassCallback is not null)
                    {
                        // Poisoned-on-arrival messages bypass the normal Dispatcher scope,
                        // so we create a fresh async scope here instead of using the root provider.
                        // RetryHelper.InvokeOnExhaustedAsync applies the configured OnExhaustedTimeout
                        // and swallows handler exceptions; pass the group/host shutdown token so a
                        // cooperative callback can short-circuit when the consumer is stopping.
                        await using var exhaustedScope = serviceScopeFactory.CreateAsyncScope();

                        using var tenantScope = TenantContextScope.ChangeFromEnvelope(
                            exhaustedScope.ServiceProvider,
                            message,
                            _logger
                        );

                        await RetryHelper
                            .InvokeOnExhaustedAsync(
                                bypassCallback,
                                new FailedInfo
                                {
                                    ServiceProvider = exhaustedScope.ServiceProvider,
                                    MessageType = MessageType.Subscribe,
                                    Message = message,
                                    IntentType = intentType,
                                    Exception =
                                        dispatchBypassException
                                        ?? new InvalidOperationException(
                                            exceptionInfo ?? "Received message contains exception information."
                                        ),
                                    // Poisoned-on-arrival messages bypass the dispatch scope and have
                                    // no associated MediumMessage; storageId is the storage's
                                    // sentinel here too (Guid.Empty == "no row identifier"), and the
                                    // retry count is zero because no consume attempt ever ran.
                                    StorageId = Guid.Empty,
                                    RetryCount = 0,
                                },
                                _options.RetryPolicy.OnExhaustedTimeout,
                                storageId: Guid.Empty,
                                _logger,
                                hostShutdownToken
                            )
                            .ConfigureAwait(false);
                    }
                    else if (!stored)
                    {
                        if (_logger.IsEnabled(LogLevel.Information))
                        {
                            _logger.SkippingPoisonedOnExhaustedAlreadyTerminal(message.GetId());
                        }
                    }

                    _logger.ConsumerReceivedMessageAfterThreshold(
                        message.GetId(),
                        _options.RetryPolicy.MaxPersistedRetries
                    );

                    _TracingAfter(tracingTimestamp, transportMessage, intentType, _serverAddress, hostShutdownToken);
                }
                else
                {
                    var mediumMessage = await _storage
                        .StoreReceivedMessageAsync(
                            name,
                            group,
                            new MediumMessage
                            {
                                StorageId = Guid.Empty,
                                Origin = message,
                                Content = string.Empty,
                                IntentType = intentType,
                            },
                            CancellationToken.None
                        )
                        .ConfigureAwait(false);
                    mediumMessage.Origin = message;

                    _TracingAfter(tracingTimestamp, transportMessage, intentType, _serverAddress, hostShutdownToken);

                    await _dispatcher
                        .EnqueueToExecute(mediumMessage, executor, CancellationToken.None)
                        .ConfigureAwait(false);
                    probeOutcomeTransferred = true;

                    await client.CommitAsync(sender).ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                _logger.LogProcessReceivedMessageFailed(e, transportMessage);

                await client.RejectAsync(sender).ConfigureAwait(false);

                _TracingError(
                    tracingTimestamp,
                    transportMessage,
                    intentType,
                    client.BrokerAddress,
                    e,
                    hostShutdownToken
                );
            }
            finally
            {
                if (probeAcquired && !probeOutcomeTransferred)
                {
                    _circuitBreakerStateManager?.ReleaseHalfOpenProbe(handleName);
                }
            }
        };
    }

    private void _WriteLog(LogMessageEventArgs logMessage)
    {
        var reason = LogSanitizer.Sanitize(logMessage.Reason) ?? string.Empty;

        switch (logMessage.LogType)
        {
            case MqLogType.ConsumerCancelled:
                _isHealthy = false;
                _logger.RabbitMqConsumerCancelled(reason);
                break;
            case MqLogType.ConsumerRegistered:
                _isHealthy = true;
                _logger.RabbitMqConsumerRegistered(reason);
                break;
            case MqLogType.ConsumerUnregistered:
                _logger.RabbitMqConsumerUnregistered(reason);
                break;
            case MqLogType.ConsumerShutdown:
                _isHealthy = false;
                _logger.RabbitMqConsumerShutdown(reason);
                break;
            case MqLogType.ConsumeError:
                _logger.KafkaClientConsumeError(reason);
                break;
            case MqLogType.ConsumeRetries:
                _logger.KafkaClientConsumeRetrying(reason);
                break;
            case MqLogType.ServerConnError:
                _isHealthy = false;
                _logger.KafkaServerConnectionError(reason);
                break;
            case MqLogType.ExceptionReceived:
                _logger.AzureServiceBusSubscriberReceivedError(reason);
                break;
            case MqLogType.AsyncErrorEvent:
                _logger.NatsSubscriberReceivedError(reason);
                break;
            case MqLogType.ConnectError:
                _isHealthy = false;
                _logger.NatsServerConnectionError(reason);
                break;
            case MqLogType.InvalidIdFormat:
                _logger.AmazonSqsInvalidIdFormat(reason);
                break;
            case MqLogType.MessageNotInflight:
                _logger.AmazonSqsMessageNotInflight(reason);
                break;
            case MqLogType.RedisConsumeError:
                _isHealthy = true;
                _logger.RedisClientConsumeError(reason);
                break;
            case MqLogType.TransportConfigurationWarning:
                _logger.TransportConfigurationWarning(reason);
                break;
            default:
                throw new InvalidOperationException($"Unknown {nameof(MqLogType)}={logMessage.LogType}");
        }
    }

    private enum LifecycleState
    {
        NotStarted = 0,
        Starting = 1,
        Running = 2,
        Disposing = 3,
        Disposed = 4,
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
        public ConcurrentBag<Task> ConsumerTasks { get; init; } = [];

        public bool IsPaused
        {
            get
            {
                lock (_clientsLock)
                {
                    return _isPaused;
                }
            }
            set
            {
                lock (_clientsLock)
                {
                    _isPaused = value;
                }
            }
        }

        public async ValueTask AddClientAsync(IConsumerClient client)
        {
            bool shouldPause;
            bool shouldDispose;
            lock (_clientsLock)
            {
                if (_disposing)
                {
                    shouldDispose = true;
                    shouldPause = false;
                }
                else
                {
                    shouldDispose = false;
                    _clients.Add(client);
                    shouldPause = _isPaused;
                }
            }

            if (shouldDispose)
            {
                // Already shutting down — dispose outside the lock so we can properly await.
                await client.DisposeAsync().ConfigureAwait(false);
                return;
            }

            if (shouldPause)
            {
                try
                {
                    await client.PauseAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Logger.LogPauseNewlyAddedClientFailed(ex, GroupName);
                }
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
            await Cts.CancelAsync().ConfigureAwait(false);
            Cts.Dispose();

            IConsumerClient[] snapshot;
            lock (_clientsLock)
            {
                _disposing = true;
                snapshot = [.. _clients];
            }

            foreach (var client in snapshot)
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    #region Tracing

    private long? _TracingBefore(
        TransportMessage message,
        IntentType intentType,
        BrokerAddress broker,
        CancellationToken cancellationToken
    )
    {
        if (_DiagnosticListener.IsEnabled(MessageDiagnosticListenerNames.BeforeConsume))
        {
            var eventData = new MessageEventDataSubStore
            {
                OperationTimestamp = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds(),
                Operation = message.GetName(),
                BrokerAddress = broker,
                TransportMessage = message,
                IntentType = intentType,
                CancellationToken = cancellationToken,
            };

            _DiagnosticListener.Write(MessageDiagnosticListenerNames.BeforeConsume, eventData);

            return eventData.OperationTimestamp;
        }

        return null;
    }

    private void _TracingAfter(
        long? tracingTimestamp,
        TransportMessage message,
        IntentType intentType,
        BrokerAddress broker,
        CancellationToken cancellationToken
    )
    {
        MessageEventCounterSource.Log.WriteConsumeMetrics();
        if (tracingTimestamp != null && _DiagnosticListener.IsEnabled(MessageDiagnosticListenerNames.AfterConsume))
        {
            var now = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
            var eventData = new MessageEventDataSubStore
            {
                OperationTimestamp = now,
                Operation = message.GetName(),
                BrokerAddress = broker,
                TransportMessage = message,
                IntentType = intentType,
                ElapsedTimeMs = now - tracingTimestamp.Value,
                CancellationToken = cancellationToken,
            };

            _DiagnosticListener.Write(MessageDiagnosticListenerNames.AfterConsume, eventData);
        }
    }

    private void _TracingError(
        long? tracingTimestamp,
        TransportMessage message,
        IntentType intentType,
        BrokerAddress broker,
        Exception ex,
        CancellationToken cancellationToken
    )
    {
        if (tracingTimestamp != null && _DiagnosticListener.IsEnabled(MessageDiagnosticListenerNames.ErrorConsume))
        {
            var now = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();

            var eventData = new MessageEventDataSubStore
            {
                OperationTimestamp = now,
                Operation = message.GetName(),
                BrokerAddress = broker,
                TransportMessage = message,
                IntentType = intentType,
                ElapsedTimeMs = now - tracingTimestamp.Value,
                Exception = ex,
                CancellationToken = cancellationToken,
            };

            _DiagnosticListener.Write(MessageDiagnosticListenerNames.ErrorConsume, eventData);
        }
    }

    #endregion
}

internal static partial class ConsumerRegisterLog
{
    [LoggerMessage(
        EventId = 1,
        EventName = "ProcessReceivedMessageFailed",
        Level = LogLevel.Error,
        Message = "An exception occurred when process received message. Message:'{Message}'."
    )]
    public static partial void LogProcessReceivedMessageFailed(
        this ILogger logger,
        Exception exception,
        TransportMessage message
    );

    [LoggerMessage(
        EventId = 2,
        EventName = "PauseNewlyAddedClientFailed",
        Level = LogLevel.Error,
        Message = "Failed to pause newly added consumer client for group '{GroupName}'."
    )]
    public static partial void LogPauseNewlyAddedClientFailed(
        this ILogger logger,
        Exception exception,
        string groupName
    );
}
