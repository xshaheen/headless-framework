// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Headless.Messaging.CircuitBreaker;
using Headless.Messaging.Configuration;
using Headless.Messaging.Diagnostics;
using Headless.Messaging.Exceptions;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.Messaging.Retry;
using Headless.Messaging.Runtime;
using Headless.Messaging.Serialization;
using Headless.Messaging.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Internal;

/// <summary>
/// Handler received message of subscribed.
/// </summary>
internal interface IConsumerRegister : IProcessingServer
{
    bool IsHealthy();

    ValueTask ReStartAsync(bool force = false, CancellationToken cancellationToken = default);
    ValueTask OnTopologyChangedAsync(CancellationToken cancellationToken = default);
}

internal sealed class ConsumerRegister(
    ILogger<ConsumerRegister> logger,
    IServiceProvider serviceProvider,
    IServiceScopeFactory serviceScopeFactory
) : IConsumerRegister, IProcessingServerShutdown
{
    private static readonly TimeSpan _RestartShutdownTimeout = TimeSpan.FromSeconds(2);

    private readonly ConcurrentDictionary<string, GroupHandle> _groupHandles = new(StringComparer.Ordinal);
    private readonly ILogger _logger = logger;
    private readonly MessagingOptions _options = serviceProvider.GetRequiredService<IOptions<MessagingOptions>>().Value;
    private readonly TimeProvider _timeProvider = serviceProvider.GetRequiredService<TimeProvider>();
    private readonly MessagingTelemetry _telemetry =
        serviceProvider.GetService<MessagingTelemetry>() ?? MessagingTelemetry.Default;
    private readonly InboxMetricPolicy _inboxMetricPolicy =
        serviceProvider.GetService<InboxMetricPolicy>() ?? new InboxMetricPolicy(IncludeTenantId: false);
    private readonly IMessagingCapabilityModel _capabilityModel =
        serviceProvider.GetRequiredService<IMessagingCapabilityModel>();
    private readonly TimeSpan _pollingDelay = TimeSpan.FromSeconds(1);

    private ICircuitBreakerStateManager? _circuitBreakerStateManager;
    private readonly IMiddlewareDescriptorRegistry? _middlewareDescriptorRegistry =
        serviceProvider.GetService<IMiddlewareDescriptorRegistry>();
    private IConsumerClientFactory _consumerClientFactory = null!;
#pragma warning disable CA2213 // Disposed through the remaining-budget DisposeAsync(TimeSpan) overload.
    private IDispatcher _dispatcher = null!;
#pragma warning restore CA2213
    private int _state = (int)LifecycleState.NotStarted;
    private readonly Lock _shutdownLock = new();
#pragma warning disable CA2213 // Disposed under the gate in _TeardownUnderGateAsync after the drain-reacquire.
    private readonly SemaphoreSlim _restartGate = new(1, 1);
#pragma warning restore CA2213
    private Task? _shutdownTask;
    private Task? _quiesceTask;
    private volatile bool _isHealthy = true;
    private int _pendingTopologyRefresh;

    private MethodMatcherCache _selector = null!;
    private ISerializer _serializer = null!;
    private BrokerAddress _serverAddress;
    private CancellationToken _hostStoppingToken;
#pragma warning disable CA2213 // Disposed under the gate in _TeardownUnderGateAsync after the drain-reacquire.
    private CancellationTokenSource _stoppingCts = new();
#pragma warning restore CA2213
    private CancellationTokenRegistration _stoppingCtsRegistration;
    private IDataStorage _storage = null!;

    public bool IsHealthy()
    {
        return _isHealthy;
    }

    public async ValueTask StartAsync(CancellationToken stoppingToken)
    {
        lock (_shutdownLock)
        {
            if (
                _shutdownTask is not null
                || (LifecycleState)Volatile.Read(ref _state) is LifecycleState.Disposing or LifecycleState.Disposed
            )
            {
                return;
            }

            _hostStoppingToken = stoppingToken;
            _stoppingCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            _stoppingCtsRegistration = _stoppingCts.Token.Register(_OnCancellationRequested);
            Interlocked.Exchange(ref _state, (int)LifecycleState.Starting);
        }

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
                if (
                    Interlocked.CompareExchange(ref _state, (int)LifecycleState.Running, (int)LifecycleState.Starting)
                    == (int)LifecycleState.Starting
                )
                {
                    await _DrainPendingTopologyRefreshesAsync().ConfigureAwait(false);
                }
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
            // Host cancellation can race this failure path with DisposeAsync. Only the startup
            // owner may reset state; once disposal wins, it owns cleanup and the terminal state.
            if ((LifecycleState)Volatile.Read(ref _state) == LifecycleState.Starting)
            {
                try
                {
                    await PulseAsync().ConfigureAwait(false);
                }
#pragma warning disable ERP022 // Best-effort cleanup — state reset below prevents stale handles from being accessible.
                // ReSharper disable once EmptyGeneralCatchClause
                catch { }
#pragma warning restore ERP022

                Interlocked.CompareExchange(ref _state, (int)LifecycleState.NotStarted, (int)LifecycleState.Starting);
            }

            Interlocked.Exchange(ref _pendingTopologyRefresh, 0);
            throw;
        }
    }

    public async ValueTask ReStartAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        if ((LifecycleState)Volatile.Read(ref _state) is LifecycleState.Disposing or LifecycleState.Disposed)
        {
            return;
        }

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

    public ValueTask DisposeAsync()
    {
        return new ValueTask(_StartShutdown(_options.ShutdownTimeout));
    }

    void IProcessingServerShutdown.Quiesce()
    {
        _Quiesce();
    }

    ValueTask IProcessingServerShutdown.StopAsync(TimeSpan timeout)
    {
        return new ValueTask(_StartShutdown(timeout));
    }

    private Task _StartShutdown(TimeSpan timeout)
    {
        _Quiesce();

        TaskCompletionSource completion;
        Task shutdownTask;
        Task quiesceTask;

        lock (_shutdownLock)
        {
            if (_shutdownTask is not null)
            {
                return _shutdownTask;
            }

            completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            shutdownTask = completion.Task;
            _shutdownTask = shutdownTask;
            quiesceTask = _quiesceTask ?? Task.CompletedTask;
        }

        _ = _RunShutdownAsync(timeout, quiesceTask, completion);
        return shutdownTask;
    }

    private void _Quiesce()
    {
        lock (_shutdownLock)
        {
            Interlocked.Exchange(ref _state, (int)LifecycleState.Disposing);
            _quiesceTask ??= _stoppingCts.CancelAsync();
        }
    }

    private async Task _RunShutdownAsync(TimeSpan timeout, Task quiesceTask, TaskCompletionSource completion)
    {
        try
        {
            try
            {
#pragma warning disable VSTHRD003 // Quiesce starts this generation-owned CancelAsync task before drain begins.
                await quiesceTask.ConfigureAwait(false);
#pragma warning restore VSTHRD003
            }
            catch (ObjectDisposedException)
            {
                // A concurrent restart already drained and disposed this exact generation.
            }

            await _DisposeCoreAsync(timeout).ConfigureAwait(false);
            completion.TrySetResult();
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }
    }

    private async Task _DisposeCoreAsync(TimeSpan timeout)
    {
        var shutdownStarted = _timeProvider.GetTimestamp();

        if (
            !await _restartGate
                .WaitAsync(_GetRemainingTimeout(shutdownStarted, timeout), CancellationToken.None)
                .ConfigureAwait(false)
        )
        {
            _logger.ProcessorStopFailed(
                new TimeoutException("The restart gate was not released before the shutdown deadline."),
                nameof(ConsumerRegister)
            );

            // A restart still owns the gate, so this path must not release or dispose it. Detach the
            // real teardown to acquire the gate unbounded in the background; returning here is the
            // intended behavior: _RunShutdownAsync completes its TCS when this method returns, so the
            // host-facing shutdown stays bounded while the eventual cleanup continues fault-observed
            // until the lifecycle reaches Disposed.
            _ = _RunEventualTeardownAsync(shutdownStarted, timeout);
            return;
        }

        await _TeardownUnderGateAsync(shutdownStarted, timeout).ConfigureAwait(false);
    }

    private async Task _RunEventualTeardownAsync(long shutdownStarted, TimeSpan timeout)
    {
        try
        {
            await _restartGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            await _TeardownUnderGateAsync(shutdownStarted, timeout).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.ProcessorStopFailed(ex, nameof(ConsumerRegister));
        }
    }

    private async Task _TeardownUnderGateAsync(long shutdownStarted, TimeSpan timeout)
    {
        try
        {
            if (!await PulseAsync(waitTimeout: _GetRemainingTimeout(shutdownStarted, timeout)).ConfigureAwait(false))
            {
                // The drain budget ran out before PulseAsync reached its handle-disposal and
                // finalization stages. This generation is terminal (final disposal, never a restart),
                // so still initiate broker-client shutdown and finalization best-effort in the
                // background instead of abandoning the clients outright.
                _ = _ObserveBestEffortHandleTeardownAsync(_groupHandles.Values.ToArray());
            }
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
            try
            {
                // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
                if (_dispatcher is not null)
                {
                    await _dispatcher
                        .DisposeAsync(_GetRemainingTimeout(shutdownStarted, timeout), _hostStoppingToken)
                        .ConfigureAwait(false);
                }
            }
            finally
            {
                Interlocked.Exchange(ref _state, (int)LifecycleState.Disposed);
                _restartGate.Release();

                // Disposal marked the lifecycle terminal before releasing the gate, so no new
                // restart can enqueue. Reacquiring lets every restart already queued at that
                // boundary observe Disposed and leave before the synchronization primitive closes.
                await _restartGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                _stoppingCts.Dispose();
                _restartGate.Dispose();
            }
        }
    }

    private async Task _ObserveBestEffortHandleTeardownAsync(IReadOnlyCollection<GroupHandle> handles)
    {
        try
        {
            // GroupHandle.DisposeAsync is idempotent via its cached dispose task; TimeSpan.Zero bounds
            // each client ShutdownAsync to its immediate path so shutdown is at least initiated.
            await Task.WhenAll(handles.Select(handle => handle.DisposeAsync(TimeSpan.Zero).AsTask()))
                .ConfigureAwait(false);

            await _FinalizePulseAsync(handles, removeCircuitState: true, _stoppingCtsRegistration, _stoppingCts)
                .ConfigureAwait(false);

            _groupHandles.Clear();
        }
        catch (Exception ex)
        {
            _logger.ProcessorStopFailed(ex, nameof(ConsumerRegister));
        }
    }

    public async Task<bool> PulseAsync(bool removeCircuitState = true, TimeSpan? waitTimeout = null)
    {
        var shutdownTimeout = waitTimeout ?? _RestartShutdownTimeout;
        var shutdownStarted = _timeProvider.GetTimestamp();
        var handles = _groupHandles.Values.ToArray();

        // Signal every group concurrently so one slow cancellation callback cannot delay the others.
        if (handles.Length > 0)
        {
            var cancellationTask = Task.WhenAll(handles.Select(handle => handle.CancelAsync().AsTask()));
            if (
                !await _WaitWithinShutdownBudgetAsync(cancellationTask, shutdownStarted, shutdownTimeout)
                    .ConfigureAwait(false)
            )
            {
                return false;
            }
        }

        // Wait for all consumer tasks to complete
        var allTasks = handles.SelectMany(handle => handle.ConsumerTasks).ToArray();
        if (allTasks.Length > 0)
        {
            try
            {
                if (
                    !await _WaitWithinShutdownBudgetAsync(Task.WhenAll(allTasks), shutdownStarted, shutdownTimeout)
                        .ConfigureAwait(false)
                )
                {
                    return false;
                }
            }
#pragma warning disable ERP022, RCS1075 // Listener cancellation/failure must not prevent client cleanup.
            catch (Exception)
            {
                // ignored
            }
#pragma warning restore ERP022, RCS1075
        }

        // Dispose all handles; only remove circuit state on final teardown,
        // not on transport restarts where state must survive broker reconnects.
        if (handles.Length > 0)
        {
            var remaining = _GetRemainingTimeout(shutdownStarted, shutdownTimeout);
            var disposalTask = Task.WhenAll(handles.Select(handle => handle.DisposeAsync(remaining).AsTask()));
            if (
                !await _WaitWithinShutdownBudgetAsync(disposalTask, shutdownStarted, shutdownTimeout)
                    .ConfigureAwait(false)
            )
            {
                return false;
            }
        }

        var finalizationTask = _FinalizePulseAsync(handles, removeCircuitState, _stoppingCtsRegistration, _stoppingCts);
        if (
            !await _WaitWithinShutdownBudgetAsync(finalizationTask, shutdownStarted, shutdownTimeout)
                .ConfigureAwait(false)
        )
        {
            return false;
        }

        _groupHandles.Clear();
        return true;
    }

    private async Task _FinalizePulseAsync(
        IReadOnlyCollection<GroupHandle> handles,
        bool removeCircuitState,
        CancellationTokenRegistration stoppingRegistration,
        CancellationTokenSource stoppingCts
    )
    {
        if (removeCircuitState && _circuitBreakerStateManager is not null)
        {
            await Task.WhenAll(
                    handles.Select(handle => _circuitBreakerStateManager.RemoveGroupAsync(handle.GroupName).AsTask())
                )
                .ConfigureAwait(false);
        }

        // Dispose the token registration before disposing the CTS to prevent accumulated callbacks
        // across successive restarts. Snapshots keep eventual cleanup isolated from replacement state.
        await stoppingRegistration.DisposeAsync().ConfigureAwait(false);
        stoppingCts.Dispose();
    }

    private TimeSpan _GetRemainingTimeout(long started, TimeSpan timeout)
    {
        var remaining = timeout - _timeProvider.GetElapsedTime(started);
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

#pragma warning disable VSTHRD003 // The caller-created task is explicitly deadline-bounded or fault-observed below.
    private async Task<bool> _WaitWithinShutdownBudgetAsync(Task task, long started, TimeSpan timeout)
    {
        if (task.IsCompleted)
        {
            await task.ConfigureAwait(false);
            return true;
        }

        var remaining = _GetRemainingTimeout(started, timeout);
        if (remaining == TimeSpan.Zero)
        {
            task.Forget();
            return false;
        }

        try
        {
            await task.WaitAsync(remaining, _timeProvider, CancellationToken.None).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            task.Forget();
            return false;
        }
    }
#pragma warning restore VSTHRD003

    public async ValueTask ExecuteAsync()
    {
        var groupingMatches = _selector.GetCandidatesMethodsOfLaneGroupNameGrouped();
        List<Task>? startupTasks = null;

        // Arm the OTel cardinality guard so unrecognized group names are rejected.
        _circuitBreakerStateManager?.RegisterKnownGroups(groupingMatches.Keys.Select(_CreateHandleName));

        foreach (var matchGroup in groupingMatches)
        {
            var groupKey = matchGroup.Key;
            var groupName = groupKey.GroupName;
            var lane = groupKey.Lane;
            var handleName = _CreateHandleName(groupKey);
            var limit = _selector.GetGroupConcurrentLimit(groupKey);

            ICollection<string> messageNames;
            try
            {
                await using var client = await _CreateConsumerClientAsync(groupName, limit, lane, _stoppingCts.Token)
                    .ConfigureAwait(false);
                client.AttachCallbacks(onMessage: null, onLog: _WriteLog);
                messageNames = await client
                    .FetchMessageNamesAsync(matchGroup.Value.Select(x => x.MessageName), _stoppingCts.Token)
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
                onPause: epoch => _PauseGroupAsync(handle, epoch),
                onResume: epoch => _ResumeGroupAsync(handle, epoch)
            );

            // Normalize HalfOpen → Open: the aborted probe is invalid on rebuilt transport clients.
            // This is a no-op during initial startup (no groups are in HalfOpen then).
            if (_circuitBreakerStateManager is not null)
            {
                await _circuitBreakerStateManager.AbortHalfOpenProbeAsync(handleName).ConfigureAwait(false);

                // If the circuit is Open (or was just re-normalized from HalfOpen),
                // pre-pause the new handle so newly created clients get paused via AddClientAsync.
                if (_circuitBreakerStateManager.TryGetOpenEpoch(handleName, out var openEpoch))
                {
                    await _PauseGroupAsync(handle, openEpoch).ConfigureAwait(false);
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
                                var innerClient = await _CreateConsumerClientAsync(
                                        groupName,
                                        limit,
                                        lane,
                                        groupCts.Token
                                    )
                                    .ConfigureAwait(false);

                                await handle.AddClientAsync(innerClient).ConfigureAwait(false);

                                _serverAddress = innerClient.BrokerAddress;

                                _RegisterMessageProcessor(innerClient, groupName, handleName, lane, groupCts.Token);

                                await innerClient.SubscribeAsync(messageNames, groupCts.Token).ConfigureAwait(false);
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
                                _isHealthy = false;
                                _logger.ConsumerProcessingLoopFailed(e);
                            }
                        },
                        CancellationToken.None,
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
        MessageLane lane,
        CancellationToken cancellationToken
    )
    {
        return _consumerClientFactory.CreateAsync(groupName, groupConcurrent, lane, cancellationToken);
    }

    private static string _CreateHandleName(ConsumerGroupKey groupKey)
    {
        return CircuitBreakerGroupKeys.For(groupKey.Lane, groupKey.GroupName);
    }

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

        if (Interlocked.CompareExchange(ref _state, (int)LifecycleState.Starting, (int)current) != (int)current)
        {
            return;
        }

        Interlocked.Exchange(ref _pendingTopologyRefresh, 0);

        // Preserve circuit breaker state across transport restarts — broker reconnects
        // are orthogonal to handler failures tracked by the circuit breaker.
        if (!await PulseAsync(removeCircuitState: false).ConfigureAwait(false))
        {
            _isHealthy = false;
            _logger.ProcessorStopFailed(
                new TimeoutException("The previous consumer generation did not stop before the restart deadline."),
                nameof(ConsumerRegister)
            );
            Interlocked.CompareExchange(ref _state, (int)LifecycleState.Running, (int)LifecycleState.Starting);
            return;
        }

        // Final shutdown can win while the old clients are draining. Re-check the lifecycle
        // before allocating replacement state so a queued stop cannot be undone by this restart.
        lock (_shutdownLock)
        {
            current = (LifecycleState)Volatile.Read(ref _state);
            if (current is LifecycleState.Disposing or LifecycleState.Disposed)
            {
                return;
            }

            _stoppingCts = CancellationTokenSource.CreateLinkedTokenSource(_hostStoppingToken);
            _stoppingCtsRegistration = _stoppingCts.Token.Register(_OnCancellationRequested);
        }

        _isHealthy = true;

        try
        {
            await ExecuteAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when ((LifecycleState)Volatile.Read(ref _state) is LifecycleState.Disposing or LifecycleState.Disposed)
        {
            // Final shutdown canceled the replacement generation after it won publication.
            // The shutdown owner will drain and dispose the captured handles under the gate.
            return;
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

            Interlocked.CompareExchange(ref _state, (int)LifecycleState.NotStarted, (int)LifecycleState.Starting);
            Interlocked.Exchange(ref _pendingTopologyRefresh, 0);
            throw;
        }

        Interlocked.CompareExchange(ref _state, (int)LifecycleState.Running, (int)LifecycleState.Starting);
    }

    private async ValueTask _DrainPendingTopologyRefreshesAsync()
    {
        while (Interlocked.Exchange(ref _pendingTopologyRefresh, 0) == 1)
        {
            await _RestartCoreAsync().ConfigureAwait(false);
        }
    }

    private ValueTask _PauseGroupAsync(GroupHandle handle, long epoch)
    {
        return _ApplyGroupIntentAsync(handle, pause: true, epoch);
    }

    private ValueTask _ResumeGroupAsync(GroupHandle handle, long epoch)
    {
        return _ApplyGroupIntentAsync(handle, pause: false, epoch);
    }

    private async ValueTask _ApplyGroupIntentAsync(GroupHandle handle, bool pause, long epoch)
    {
        if (
            handle.IsDisposing
            || (LifecycleState)Volatile.Read(ref _state) is LifecycleState.Disposing or LifecycleState.Disposed
        )
        {
            return;
        }

        try
        {
            await handle.ApplyGate.WaitAsync(handle.Cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            if (handle.IsDisposing || epoch < handle.LastAppliedEpoch)
            {
                _logger.StaleCircuitIntentSkipped(handle.GroupName, epoch, handle.LastAppliedEpoch);
                return;
            }

            if (pause)
            {
                await _PauseClientsAsync(handle).ConfigureAwait(false);
            }
            else
            {
                await _ResumeClientsAsync(handle).ConfigureAwait(false);
            }

            handle.LastAppliedEpoch = epoch;
        }
        finally
        {
            handle.ApplyGate.Release();
        }
    }

    private async ValueTask _PauseClientsAsync(GroupHandle handle)
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

    private async ValueTask _ResumeClientsAsync(GroupHandle handle)
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
        MessageLane lane,
        CancellationToken hostShutdownToken
    )
    {
        async Task onMessageCallback(TransportMessage transportMessage, object? sender)
        {
            long? probeEpoch = null;
            var admissionEpoch = 0L;
            var probeOutcomeTransferred = false;
            var transportSettled = false;
            MessagingTraceHandle traceHandle = default;

            // Exactly one consume outcome (success or error) may be recorded per message: the trace handle is an
            // immutable struct, so this flag is what keeps the subscriber-not-found path (error emitted inline,
            // then routed to the poison store) from also emitting the success outcome on the same handle.
            var consumeOutcomeRecorded = false;

            // Receive-stage state: the context exists only when receive middleware ran for this
            // delivery; the two flags below let the poison and cancellation handlers distinguish an
            // explicit policy decision (Reject/Skip) from a middleware fault, and a cancellation
            // bound to the receive token from a foreign one.
            ReceiveContext? receiveContext = null;
            var receiveRejectIsPolicy = false;
            var receiveOutcomeCancelled = false;

            try
            {
                if (_circuitBreakerStateManager is not null)
                {
                    probeEpoch = _circuitBreakerStateManager.TryAcquireHalfOpenProbe(handleName);

                    if (probeEpoch is null)
                    {
                        // Settlement is must-complete: never abandon a reject on host shutdown.
                        await client.RejectAsync(sender, CancellationToken.None).ConfigureAwait(false);

                        return;
                    }

                    admissionEpoch = probeEpoch.Value;
                    if (_circuitBreakerStateManager.TryGetOpenEpoch(handleName, out var openEpoch))
                    {
                        var handle = _groupHandles.TryGetValue(handleName, out var currentHandle)
                            ? currentHandle
                            : null;
                        var safeGroupName = LogSanitizer.Sanitize(handleName);
                        if (handle?.IsPauseAppliedForEpoch(openEpoch) == true)
                        {
                            if (_logger.IsEnabled(LogLevel.Warning))
                            {
                                _logger.DeliveryAdmittedWhileOpenAfterPause(safeGroupName);
                            }
                        }
                        else
                        {
                            if (_logger.IsEnabled(LogLevel.Debug))
                            {
                                _logger.DeliveryAdmittedDuringPauseLatency(safeGroupName);
                            }
                        }
                    }
                }

                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    var safeMessageId = LogSanitizer.Sanitize(transportMessage.Id);
                    var safeMessageName = LogSanitizer.Sanitize(transportMessage.Name);
                    _logger.MessageReceived(safeMessageId, safeMessageName);
                }

                traceHandle = _TracingBefore(transportMessage, lane, _serverAddress);

                var name = transportMessage.Name;

                Message message;
                Exception? dispatchBypassException = null;

                var canFindSubscriber = _selector.TryGetMessageNameExecutor(name, group, lane, out var executor);
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

                        _TracingError(traceHandle, transportMessage, client.BrokerAddress, ex);
                        consumeOutcomeRecorded = true;

                        throw ex;
                    }

                    // The receive ring: receive middleware wraps the inner steps (contract-version
                    // validation, deserialization, null-payload check) when any descriptor matches;
                    // otherwise the inner steps run directly and the delivery path is byte-for-byte
                    // today's zero-middleware behavior.
                    var receiveOutcome = await _RunReceiveRingAsync(
                            transportMessage,
                            executor!,
                            group,
                            lane,
                            _RunInnerReceiveAsync,
                            hostShutdownToken
                        )
                        .ConfigureAwait(false);

                    if (receiveOutcome.Context is { } ringContext)
                    {
                        receiveContext = ringContext;
                    }

                    if (receiveOutcome.Result == ReceiveRingResult.Skipped)
                    {
                        // Skip commits and drops: no storage row, no exhausted callback, and the
                        // half-open probe is released by the callback's finally (neither success nor
                        // failure — a header-based filter decision must not advance the breaker).
                        if (_logger.IsEnabled(LogLevel.Information))
                        {
                            _logger.ReceiveMessageSkipped(
                                receiveOutcome.MiddlewareType!,
                                LogSanitizer.Sanitize(transportMessage.Id),
                                LogSanitizer.Sanitize(name),
                                LogSanitizer.Sanitize(group),
                                lane.ToString(),
                                LogSanitizer.Sanitize(receiveOutcome.OutcomeReason)
                            );
                        }
                        MessagingMetrics.RecordReceiveOutcome("skipped");
                        traceHandle.Activity?.SetTag(MessagingMetrics.TagReceiveOutcome, "skipped");

                        _TracingAfter(traceHandle, transportMessage, _serverAddress);
                        consumeOutcomeRecorded = true;

                        // Settlement is must-complete: never abandon a commit on host shutdown.
                        await client.CommitAsync(sender, CancellationToken.None).ConfigureAwait(false);
                        transportSettled = true;
                        return;
                    }

                    if (receiveOutcome.Result == ReceiveRingResult.Cancelled)
                    {
                        receiveOutcomeCancelled = true;
                        MessagingMetrics.RecordReceiveOutcome("cancelled");
                        traceHandle.Activity?.SetTag(MessagingMetrics.TagReceiveOutcome, "cancelled");
                        if (_logger.IsEnabled(LogLevel.Debug))
                        {
                            _logger.ReceiveOutcomeCancelled(
                                LogSanitizer.Sanitize(transportMessage.Id),
                                LogSanitizer.Sanitize(name),
                                LogSanitizer.Sanitize(group)
                            );
                        }
                        throw receiveOutcome.Exception!;
                    }

                    if (receiveOutcome.Result == ReceiveRingResult.Rejected)
                    {
                        dispatchBypassException = receiveOutcome.Exception!;
                        receiveRejectIsPolicy = receiveOutcome.IsPolicyReject;
                        exceptionInfo = dispatchBypassException.ExpandMessage();
                        message = _BuildPoisonMessage(transportMessage, receiveContext, dispatchBypassException);

                        MessagingMetrics.RecordReceiveOutcome("rejected");
                        traceHandle.Activity?.SetTag(MessagingMetrics.TagReceiveOutcome, "rejected");
                        if (_logger.IsEnabled(LogLevel.Warning))
                        {
                            _logger.ReceiveMessageRejected(
                                dispatchBypassException is ReceiveMessageRejectedException
                                    ? null
                                    : dispatchBypassException,
                                receiveOutcome.MiddlewareType!,
                                LogSanitizer.Sanitize(transportMessage.Id),
                                LogSanitizer.Sanitize(name),
                                LogSanitizer.Sanitize(group),
                                lane.ToString(),
                                LogSanitizer.Sanitize(
                                    receiveOutcome.OutcomeReason ?? dispatchBypassException.ExpandMessage()
                                )
                            );
                        }

                        // A middleware reject is a policy decision on this delivery, not a subscriber
                        // fault: finalize the span as an error here (mirroring subscriber-not-found)
                        // so the shared poison block below does not emit a success stop for it.
                        if (!consumeOutcomeRecorded)
                        {
                            _TracingError(traceHandle, transportMessage, client.BrokerAddress, dispatchBypassException);
                            consumeOutcomeRecorded = true;
                        }
                    }
                    else
                    {
                        message = receiveOutcome.Message!;

                        // The delivery continued to admission; the ring's copy-on-write headers/body
                        // (already reflected in the deserialized message) are what storage persists.
                        MessagingMetrics.RecordReceiveOutcome("accepted");
                        traceHandle.Activity?.SetTag(MessagingMetrics.TagReceiveOutcome, "accepted");
                    }
                }
                catch (Exception e) when (!receiveOutcomeCancelled)
                {
                    dispatchBypassException = e;
                    receiveRejectIsPolicy = false;
                    exceptionInfo = e.ExpandMessage();
                    message = _BuildPoisonMessage(transportMessage, receiveContext, e);

                    // Every row built here is a poison-on-arrival outcome: subscriber-not-found,
                    // contract-version mismatch, a Stage A deserialization failure, or a receive
                    // middleware fault the ring converted into a reject.
                    MessagingMetrics.RecordReceiveOutcome("rejected");
                    traceHandle.Activity?.SetTag(MessagingMetrics.TagReceiveOutcome, "rejected");
                }

                if (message.HasException())
                {
                    if (dispatchBypassException is not null && _circuitBreakerStateManager is not null)
                    {
                        // An explicit Reject() (or its Stage A equivalents routed as policy) is a
                        // policy decision on attacker-controllable input, not a subscriber failure:
                        // neither report nor transfer the probe — the callback's finally releases it,
                        // exactly like Skip. Middleware faults, undeclared outcomes, and version or
                        // deserialization failures keep reporting, and the failure report owns the probe.
                        if (!receiveRejectIsPolicy)
                        {
                            await _circuitBreakerStateManager
                                .ReportFailureAsync(handleName, dispatchBypassException, CancellationToken.None)
                                .ConfigureAwait(false);

                            probeOutcomeTransferred = true;
                        }
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
                                Lane = lane,
                            },
                            exceptionInfo,
                            hostShutdownToken
                        )
                        .ConfigureAwait(false);

                    // Settlement is must-complete: never abandon a commit on host shutdown.
                    await client.CommitAsync(sender, CancellationToken.None).ConfigureAwait(false);
                    transportSettled = true;

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
                                    Lane = lane,
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
                                _timeProvider,
                                hostShutdownToken
                            )
                            .ConfigureAwait(false);
                    }
                    else if (!stored)
                    {
                        if (_logger.IsEnabled(LogLevel.Information))
                        {
                            _logger.SkippingPoisonedOnExhaustedAlreadyTerminal(message.Id);
                        }
                    }

                    _logger.ConsumerReceivedMessageAfterThreshold(message.Id, _options.RetryPolicy.MaxPersistedRetries);

                    if (consumeOutcomeRecorded)
                    {
                        // The span + consume outcome were already finalized as an error (subscriber-not-found);
                        // only the legacy EventCounter fires here so its counts match the pre-native bridge.
                        MessageEventCounterSource.Log.WriteConsumeMetrics();
                    }
                    else
                    {
                        _TracingAfter(traceHandle, transportMessage, _serverAddress);
                        consumeOutcomeRecorded = true;
                    }
                }
                else
                {
                    var consumerIdentity = executor!.ConsumerIdentity;
                    var messageContractVersion = executor.MessageContractVersion;
                    if (
                        string.IsNullOrWhiteSpace(consumerIdentity) || string.IsNullOrWhiteSpace(messageContractVersion)
                    )
                    {
                        // Runtime subscriptions are intentionally process-local and have no durable identity.
                        // Preserve their legacy delivery path; bootstrap validation prevents configured durable
                        // consumers from reaching this branch without a stable identity and contract version.
                        var runtimeMessage = await _storage
                            .StoreReceivedMessageAsync(
                                name,
                                group,
                                new MediumMessage
                                {
                                    StorageId = Guid.Empty,
                                    Origin = message,
                                    Content = string.Empty,
                                    Lane = lane,
                                },
                                CancellationToken.None
                            )
                            .ConfigureAwait(false);

                        runtimeMessage.Origin = message;
                        _TracingAfter(traceHandle, transportMessage, _serverAddress);
                        consumeOutcomeRecorded = true;

                        // The executor releases the HalfOpen probe with this epoch; without it the
                        // release is a no-op and the probe slot stays held on this path.
                        runtimeMessage.ProbeEpoch = admissionEpoch;
                        await _dispatcher
                            .EnqueueToExecute(runtimeMessage, executor, CancellationToken.None)
                            .ConfigureAwait(false);
                        probeOutcomeTransferred = true;

                        await client.CommitAsync(sender, CancellationToken.None).ConfigureAwait(false);
                        transportSettled = true;
                        return;
                    }

                    var admission = await _storage
                        .AdmitReceivedMessageAsync(
                            name,
                            group,
                            consumerIdentity,
                            message.Headers[Headers.ContractVersion]!,
                            new MediumMessage
                            {
                                StorageId = Guid.Empty,
                                Origin = message,
                                Content = string.Empty,
                                Lane = lane,
                            },
                            inboxRetention: executor.InboxRetention,
                            cancellationToken: CancellationToken.None
                        )
                        .ConfigureAwait(false);

                    admission.Message.Origin = message;

                    var storageCapability = _capabilityModel.Providers.FirstOrDefault(capability =>
                        capability.Role is MessagingProviderRole.Storage
                    );
                    if (storageCapability?.InboxCapability is { } inboxTier)
                    {
                        MessagingMetrics.RecordInbox(
                            admission.Disposition is InboxAdmissionDisposition.Winner
                                ? InboxMetricKind.Capability
                                : InboxMetricKind.Duplicate,
                            consumerIdentity,
                            lane,
                            admission.Disposition switch
                            {
                                InboxAdmissionDisposition.Winner => InboxMetricOutcome.Winner,
                                InboxAdmissionDisposition.InFlightDuplicate => InboxMetricOutcome.InFlightDuplicate,
                                InboxAdmissionDisposition.SucceededDuplicate => InboxMetricOutcome.SucceededDuplicate,
                                InboxAdmissionDisposition.TerminalFailedDuplicate =>
                                    InboxMetricOutcome.TerminalFailedDuplicate,
                                _ => throw new InvalidOperationException(
                                    $"Unsupported inbox admission disposition '{admission.Disposition}'."
                                ),
                            },
                            inboxTier,
                            storageCapability.Provider,
                            message.Headers.TryGetValue(Headers.TenantId, out var tenantId) ? tenantId : null,
                            _inboxMetricPolicy.IncludeTenantId
                        );
                    }

                    _TracingAfter(traceHandle, transportMessage, _serverAddress);
                    consumeOutcomeRecorded = true;

                    // Settlement is must-complete: never abandon a commit on host shutdown.
                    await client.CommitAsync(sender, CancellationToken.None).ConfigureAwait(false);
                    transportSettled = true;

                    if (admission.ShouldDispatch)
                    {
                        admission.Message.ProbeEpoch = admissionEpoch;
                        await _dispatcher
                            .EnqueueToExecute(admission.Message, executor, CancellationToken.None)
                            .ConfigureAwait(false);

                        probeOutcomeTransferred = true;
                    }
                }
            }
            catch (Exception e)
            {
                _logger.LogProcessReceivedMessageFailed(e, transportMessage);

                if (!transportSettled)
                {
                    // Settlement is must-complete: never abandon a reject on host shutdown.
                    await client.RejectAsync(sender, CancellationToken.None).ConfigureAwait(false);
                }

                if (e is OperationCanceledException)
                {
                    // Benign cancellation (host shutdown) is not a consume failure: stop (export) the span
                    // without an error status, matching the publish/subscriber-invoke emission sites.
                    traceHandle.Activity?.Dispose();
                }
                else if (!consumeOutcomeRecorded)
                {
                    _TracingError(traceHandle, transportMessage, client.BrokerAddress, e);
                }
            }
            finally
            {
                if (probeEpoch is not null && !probeOutcomeTransferred)
                {
                    _circuitBreakerStateManager?.ReleaseHalfOpenProbe(handleName, admissionEpoch);
                }
            }
        }

        client.AttachCallbacks(onMessageCallback, _WriteLog);
    }

    private static void _ValidateMessageContractVersion(
        IDictionary<string, string?> headers,
        ConsumerExecutorDescriptor executor
    )
    {
        var receivedVersion = headers.TryGetValue(Headers.ContractVersion, out var value)
            ? MessagingOptions.ValidateContractVersion(value ?? string.Empty)
            : MessageOptions.InitialContractVersion;

        headers[Headers.ContractVersion] = receivedVersion;

        if (
            !string.IsNullOrWhiteSpace(executor.MessageContractVersion)
            && !string.Equals(executor.MessageContractVersion, receivedVersion, StringComparison.Ordinal)
        )
        {
            throw new InvalidOperationException(
                $"Message contract version '{receivedVersion}' does not match registered message contract version "
                    + $"'{executor.MessageContractVersion}' for '{executor.MessageName}'."
            );
        }
    }

    /// <summary>
    /// The inner receive steps the ring's innermost <c>next</c> runs: contract-version validation,
    /// Stage A deserialization (wrapped in <see cref="MessageDeserializationException"/>), and the
    /// null-payload check for typed consumers.
    /// </summary>
    private async ValueTask<Message> _RunInnerReceiveAsync(
        IDictionary<string, string?> currentHeaders,
        ReadOnlyMemory<byte> currentBody,
        ConsumerExecutorDescriptor executor,
        CancellationToken cancellationToken
    )
    {
        _ValidateMessageContractVersion(currentHeaders, executor);

        var effectiveTransport = new TransportMessage(currentHeaders, currentBody);
        Message deserialized;

        // Only the deserialize call is wrapped: a serializer failure is a terminal payload defect,
        // while a version mismatch keeps its InvalidOperationException so middleware can catch it
        // and convert it into a Reject decision before it reaches the poison store.
        try
        {
            deserialized = await _serializer
                .DeserializeAsync(effectiveTransport, executor.MessageValueType, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception deserializeEx)
        {
            throw new MessageDeserializationException(
                $"Failed to deserialize the message body for '{executor.MessageName}' into "
                    + $"'{executor.MessageValueType}' (group '{executor.GroupName}'): {deserializeEx.Message}",
                deserializeEx
            );
        }

        // An empty body for a typed consumer is the same deterministic payload defect as a malformed
        // one; untyped consumers (null MessageValueType) keep Value = null passing as before.
        if (executor.MessageValueType is { } typedPayload && deserialized.Value is null)
        {
            throw new MessageDeserializationException(
                $"Empty message body for typed consumer '{executor.MessageName}' "
                    + $"(payload type '{typedPayload}', group '{executor.GroupName}')."
            );
        }

        deserialized.RemoveException();

        return deserialized;
    }

    /// <summary>
    /// Runs the receive stage for one delivery: the receive-middleware ring when descriptors match
    /// this consumer, otherwise the inner steps directly (the pre-middleware fast path — no scope,
    /// no context, no allocation beyond the outcome record).
    /// </summary>
    private async ValueTask<ReceiveRingOutcome> _RunReceiveRingAsync(
        TransportMessage transportMessage,
        ConsumerExecutorDescriptor executor,
        string group,
        MessageLane lane,
        Func<
            IDictionary<string, string?>,
            ReadOnlyMemory<byte>,
            ConsumerExecutorDescriptor,
            CancellationToken,
            ValueTask<Message>
        > innerReceive,
        CancellationToken hostShutdownToken
    )
    {
        // Runtime subscriptions can carry a null payload type (no typed handler parameter). They
        // still flow through the ring: object stands in as the context/lookup type, so global
        // receive middleware runs and only descriptors explicitly registered for object match.
        var messageType = executor.MessageValueType ?? typeof(object);
        IReadOnlyList<MiddlewareDescriptor>? descriptors = null;
        var hasDescriptors =
            _middlewareDescriptorRegistry is not null
            && _middlewareDescriptorRegistry.TryGetReceiveDescriptors(messageType, group, lane, out descriptors);

        if (!hasDescriptors)
        {
            // Zero-middleware fast path: identical to the pre-receive-ring behavior. The header
            // dictionary and body are the transport's own, exactly as before.
            return new ReceiveRingOutcome(
                ReceiveRingResult.Accepted,
                Message: await innerReceive(
                        transportMessage.Headers,
                        transportMessage.Body,
                        executor,
                        hostShutdownToken
                    )
                    .ConfigureAwait(false)
            );
        }

        var context = new ReceiveContext(
            transportMessage.Id,
            transportMessage.Name,
            group,
            lane,
            messageType,
            executor.MessageContractVersion,
            transportMessage.Headers,
            transportMessage.Body,
            hostShutdownToken
        );

        // Hoisted so every middleware's error filter captures one loop-invariant reference, and so
        // the accept path can detect a post-success throw from any ring member.
        var innerCompleted = new StrongBox<bool>(value: false);
        Message? innerMessage = null;

        Func<ValueTask> next = async () =>
        {
            // Single-shot: a second invocation would re-run version stamping and deserialization on
            // a context that has already completed.
            if (innerCompleted.Value)
            {
                throw new InvalidOperationException(
                    "The receive pipeline's next delegate can only be invoked once per delivery."
                );
            }

            // The inner steps always consume the context's CURRENT view: middleware transformations
            // (SetHeader/RemoveHeader/ReplaceBody) apply before the framework reads the envelope. The
            // received dictionary is never handed to the inner steps once a copy-on-write view exists.
            var currentHeaders = ReferenceEquals(context.Headers, transportMessage.Headers)
                ? transportMessage.Headers
                : _AsWritableHeaders(context.Headers);

            innerMessage = await innerReceive(currentHeaders, context.Body, executor, context.CancellationToken)
                .ConfigureAwait(false);

            context.MarkCompleted();
            innerCompleted.Value = true;
        };

        async ValueTask InvokeMiddleware(IReceiveMiddleware middleware, Func<ValueTask> innerNext)
        {
            var middlewareType = middleware.GetType().FullName ?? middleware.GetType().Name;

            try
            {
                await middleware.InvokeAsync(context, innerNext).ConfigureAwait(false);
                context.MarkCompleted();
            }
            catch (Exception ex) when (innerCompleted.Value)
            {
                // The inner pipeline already produced its message; suppressing the fault keeps the
                // accepted delivery instead of discarding it after its work has been done.
                _logger.ReceivePostSuccessMiddlewareFailed(ex, middlewareType);
                return;
            }
            catch (OperationCanceledException ex) when (ex.CancellationToken == context.CancellationToken)
            {
                throw new OperationCanceledException(ex.Message, ex, context.CancellationToken);
            }

            // A swapped-in token (SetCancellationToken) that became cancelled between middleware
            // returns is a cooperative stop, not a fault.
            context.CancellationToken.ThrowIfCancellationRequested();
        }

        // Per-delivery scope: middleware resolves from a fresh scope so scoped registrations see
        // per-delivery state; the ring (and the scope) complete before any storage write or dispatch.
        await using var scope = serviceScopeFactory.CreateAsyncScope();

        IReceiveMiddleware? outermost = null;
        foreach (var descriptor in descriptors!)
        {
            var middleware = scope
                .ServiceProvider.GetServices(descriptor.ServiceType)
                .FirstOrDefault(service => service?.GetType() == descriptor.MiddlewareType);
            if (middleware is not IReceiveMiddleware matched)
            {
                continue;
            }

            outermost ??= matched;
            var current = matched;
            var innerNext = next;
            next = () => InvokeMiddleware(current, innerNext);
        }

        if (outermost is null)
        {
            // Descriptors matched but every resolution came back empty (a registered service replaced
            // by a different implementation): degrade to the direct inner run rather than dropping
            // the delivery.
            return new ReceiveRingOutcome(
                ReceiveRingResult.Accepted,
                Message: await innerReceive(
                        transportMessage.Headers,
                        transportMessage.Body,
                        executor,
                        hostShutdownToken
                    )
                    .ConfigureAwait(false)
            );
        }

        var outermostType = outermost.GetType();

        try
        {
            await next().ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (ex.CancellationToken == context.CancellationToken)
        {
            return new ReceiveRingOutcome(
                ReceiveRingResult.Cancelled,
                Exception: ex,
                Context: context,
                MiddlewareType: outermostType.FullName ?? outermostType.Name
            );
        }
        catch (Exception ex)
        {
            // Faults thrown from middleware code (or the inner steps when middleware let them
            // escape) map to the Reject row; cancellation bound to the context token was handled above.
            return new ReceiveRingOutcome(
                ReceiveRingResult.Rejected,
                Exception: ex,
                Context: context,
                MiddlewareType: outermostType.FullName ?? outermostType.Name,
                OutcomeReason: context.Outcome is ReceiveOutcome.Reject ? context.OutcomeReason : null,
                IsPolicyReject: false
            );
        }

        if (innerCompleted.Value)
        {
            if (context.Outcome is ReceiveOutcome.Skip or ReceiveOutcome.Reject)
            {
                // Declaring an outcome after a completed next is contradictory — the delivery was
                // already accepted. Treat it as the undeclared-outcome fault it is.
                return new ReceiveRingOutcome(
                    ReceiveRingResult.Rejected,
                    Exception: new ReceiveOutcomeUndeclaredException(outermostType),
                    Context: context,
                    MiddlewareType: outermostType.FullName ?? outermostType.Name
                );
            }

            return new ReceiveRingOutcome(
                ReceiveRingResult.Accepted,
                Message: innerMessage,
                Context: context,
                MiddlewareType: outermostType.FullName ?? outermostType.Name
            );
        }

        // The ring returned without reaching the inner steps.
        if (context.Outcome == ReceiveOutcome.Skip)
        {
            return new ReceiveRingOutcome(
                ReceiveRingResult.Skipped,
                Context: context,
                MiddlewareType: outermostType.FullName ?? outermostType.Name,
                OutcomeReason: context.OutcomeReason
            );
        }

        var isPolicyReject = context.Outcome == ReceiveOutcome.Reject;
        var rejectException = isPolicyReject
            ? context.RejectCause ?? new ReceiveMessageRejectedException(context.OutcomeReason!)
            : new ReceiveOutcomeUndeclaredException(outermostType);

        return new ReceiveRingOutcome(
            ReceiveRingResult.Rejected,
            Exception: rejectException,
            Context: context,
            MiddlewareType: outermostType.FullName ?? outermostType.Name,
            OutcomeReason: context.OutcomeReason,
            IsPolicyReject: isPolicyReject
        );
    }

    /// <summary>
    /// Builds the poison-on-arrival envelope. The <c>data:</c> URI always carries the RECEIVED bytes
    /// (capped); the headers are the CURRENT view when receive middleware transformed the envelope
    /// (the framework's Exception stamp is allowed on that view), else the received headers.
    /// </summary>
    private Message _BuildPoisonMessage(TransportMessage transportMessage, ReceiveContext? context, Exception exception)
    {
        var headers =
            context is not null && !ReferenceEquals(context.Headers, transportMessage.Headers)
                ? _AsWritableHeaders(context.Headers)
                : transportMessage.Headers;

        headers[Headers.Exception] = exception.GetType().Name;

        return new Message(headers, _BuildCappedDataUri(transportMessage.Body, headers));
    }

    /// <summary>
    /// Builds the capped <c>data:</c> URI from the received body: whole at/below
    /// <see cref="MessagingOptions.MaxPoisonEnvelopeBytes"/>, a <c>truncated</c>-marked prefix past
    /// it, and omitted entirely beyond four times the cap (headers-only poison row). Measured on the
    /// raw bytes, before base64 encoding.
    /// </summary>
    private string? _BuildCappedDataUri(ReadOnlyMemory<byte> receivedBody, IDictionary<string, string?> headers)
    {
        if (receivedBody.Length == 0)
        {
            return null;
        }

        var mediaType =
            headers.TryGetValue(Headers.Type, out var type) && !string.IsNullOrWhiteSpace(type) ? type : "UnknownType";

        if (_options.MaxPoisonEnvelopeBytes is not { } limit)
        {
            return $"data:{mediaType};base64," + receivedBody.Span.ToBase64();
        }

        if (receivedBody.Length > 4L * limit)
        {
            // Far past the cap: even a truncated prefix would dwarf the rest of the row.
            return null;
        }

        if (receivedBody.Length <= limit)
        {
            return $"data:{mediaType};base64," + receivedBody.Span.ToBase64();
        }

        return $"data:{mediaType};truncated;base64," + receivedBody.Span[..limit].ToBase64();
    }

    private static Dictionary<string, string?> _AsWritableHeaders(IReadOnlyDictionary<string, string?> headers)
    {
        return new Dictionary<string, string?>(headers, StringComparer.Ordinal);
    }

    private enum ReceiveRingResult
    {
        Accepted = 0,
        Skipped = 1,
        Rejected = 2,
        Cancelled = 3,
    }

    private sealed record ReceiveRingOutcome(
        ReceiveRingResult Result,
        Message? Message = null,
        Exception? Exception = null,
        ReceiveContext? Context = null,
        string? MiddlewareType = null,
        string? OutcomeReason = null,
        bool IsPolicyReject = false
    );

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

    private sealed class GroupHandle
    {
        private readonly Lock _clientsLock = new();
        private Task? _disposeTask;
        private bool _disposing;
        private bool _isPaused;

        // SemaphoreSlim.Dispose never completes queued waiters and breaks the holder's release.
        // This handle-generation gate is intentionally left undisposed; disposal is signalled by
        // _disposing and checked before waiting and after acquiring the gate.
#pragma warning disable CA2213 // Never dispose: queued waiters must observe disposal and release.
        public SemaphoreSlim ApplyGate { get; } = new(1, 1);
#pragma warning restore CA2213

#pragma warning disable IDE0032 // Uses Volatile read/write for cross-thread visibility between ApplyGate and admission logging.
        private long _lastAppliedEpoch;
#pragma warning restore IDE0032

        public long LastAppliedEpoch
        {
            get => Volatile.Read(ref _lastAppliedEpoch);
            set => Volatile.Write(ref _lastAppliedEpoch, value);
        }

        private readonly List<IConsumerClient> _clients = [];

        public required ILogger Logger { get; init; }
        public required CancellationTokenSource Cts { get; init; }
        public required string GroupName { get; init; }
        public ConcurrentBag<Task> ConsumerTasks { get; init; } = [];

        // Production reads the pause state through the private _isPaused field (see AddClientAsync); the public getter
        // exists for setter symmetry and is exercised by the reflection-based ConsumerRegisterTests. Not dead state.
        // ReSharper disable once UnusedMember.Local
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

        public bool IsDisposing
        {
            get
            {
                lock (_clientsLock)
                {
                    return _disposing;
                }
            }
        }

        public bool IsPauseAppliedForEpoch(long epoch)
        {
            lock (_clientsLock)
            {
                return LastAppliedEpoch == epoch && _isPaused;
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
                    lock (_clientsLock)
                    {
                        _clients.Remove(client);
                    }

                    await client.DisposeAsync().ConfigureAwait(false);
                    throw;
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

        public async ValueTask CancelAsync()
        {
            lock (_clientsLock)
            {
                if (_disposeTask is not null)
                {
                    return;
                }
            }

            try
            {
                await Cts.CancelAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                // A concurrent idempotent disposal already canceled and retired this generation.
            }
        }

        public ValueTask DisposeAsync(TimeSpan shutdownTimeout)
        {
            lock (_clientsLock)
            {
                if (_disposeTask is { } disposeTask)
                {
                    return new ValueTask(disposeTask);
                }

                _disposing = true;
                _disposeTask = _DisposeCoreAsync([.. _clients], shutdownTimeout);
                return new ValueTask(_disposeTask);
            }
        }

        private async Task _DisposeCoreAsync(IReadOnlyCollection<IConsumerClient> clients, TimeSpan shutdownTimeout)
        {
            await Cts.CancelAsync().ConfigureAwait(false);
            Cts.Dispose();

            await Task.WhenAll(clients.Select(client => client.ShutdownAsync(shutdownTimeout).AsTask()))
                .ConfigureAwait(false);
        }
    }

    #region Tracing

    private MessagingTraceHandle _TracingBefore(TransportMessage message, MessageLane lane, BrokerAddress broker)
    {
        if (!MessagingDiagnostics.IsEnabled)
        {
            return default;
        }

        var now = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        var activity = _telemetry.ConsumeStart(message, lane, broker, now);

        return new MessagingTraceHandle(activity, now);
    }

    private void _TracingAfter(MessagingTraceHandle traceHandle, TransportMessage message, BrokerAddress broker)
    {
        MessageEventCounterSource.Log.WriteConsumeMetrics();

        if (!traceHandle.IsRecording)
        {
            return;
        }

        var now = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        MessagingTelemetry.ConsumeStop(traceHandle.Activity, message, broker, traceHandle.StartTimestampMs!.Value, now);
    }

    private static void _TracingError(
        MessagingTraceHandle traceHandle,
        TransportMessage message,
        BrokerAddress broker,
        Exception ex
    )
    {
        if (!traceHandle.IsRecording)
        {
            return;
        }

        MessagingTelemetry.ConsumeError(traceHandle.Activity, message, broker, ex);
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
