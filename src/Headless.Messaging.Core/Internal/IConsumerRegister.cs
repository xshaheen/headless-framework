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

internal sealed partial class ConsumerRegister(
    ILogger<ConsumerRegister> logger,
    IServiceProvider serviceProvider,
    IServiceScopeFactory serviceScopeFactory
) : IConsumerRegister, IProcessingServerShutdown
{
    private static readonly TimeSpan _RestartShutdownTimeout = TimeSpan.FromSeconds(2);

    private readonly ConcurrentDictionary<string, SubscriptionHandle> _subscriptionHandles = new(
        StringComparer.Ordinal
    );

    private readonly ILogger _logger = logger;
    private readonly MessagingOptions _options = serviceProvider.GetRequiredService<IOptions<MessagingOptions>>().Value;

    private readonly TimeProvider _timeProvider = serviceProvider.GetRequiredService<TimeProvider>();

    private readonly MessagingTelemetry _telemetry =
        serviceProvider.GetService<MessagingTelemetry>() ?? MessagingTelemetry.Default;

    private readonly InboxMetricPolicy _inboxMetricPolicy =
        serviceProvider.GetService<InboxMetricPolicy>() ?? new InboxMetricPolicy(TenantTagName: null);

    private readonly IMessagingCapabilityModel _capabilityModel =
        serviceProvider.GetRequiredService<IMessagingCapabilityModel>();

    private readonly TimeSpan _pollingDelay = TimeSpan.FromSeconds(1);

    private readonly Guid _instanceId = (
        serviceProvider.GetService<MessagingInstanceId>() ?? new MessagingInstanceId()
    ).Value;

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
    private ISubscribeInvoker _subscribeInvoker = null!;

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
        _subscribeInvoker = serviceProvider.GetRequiredService<ISubscribeInvoker>();

        try
        {
            var establishments = await _StartSubscriptionsAsync().ConfigureAwait(false);

            // A started host has resynchronized its every-instance consumers. Each hook is bounded, so a stuck one cannot
            // hold startup, and none holds the restart gate: a hook that changes the topology only marks a refresh here.
            await Task.WhenAll(establishments).ConfigureAwait(false);

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

    /// <summary>
    /// Applies a runtime subscription change. Only the subscription groups whose shape changed are rebuilt: an added
    /// group starts its clients, a removed one stops them, and a changed one does both. Every other group keeps its
    /// clients, so an unrelated every-instance consumer is not re-established and its hook does not run.
    /// </summary>
    public async ValueTask OnTopologyChangedAsync(CancellationToken cancellationToken = default)
    {
        var current = (LifecycleState)Volatile.Read(ref _state);

        if (current == LifecycleState.Running)
        {
            await _RefreshTopologyAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (current == LifecycleState.Starting)
        {
            Interlocked.Exchange(ref _pendingTopologyRefresh, 1);

            // A start or restart that finished between the read above and the write may already have drained, and then
            // nothing would apply this change. Claim the mark back and restart here; an establishment hook running
            // beside a restart reaches this window.
            if (
                (LifecycleState)Volatile.Read(ref _state) == LifecycleState.Running
                && Interlocked.CompareExchange(ref _pendingTopologyRefresh, 0, 1) == 1
            )
            {
                await _RefreshTopologyAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async ValueTask _RefreshTopologyAsync(CancellationToken cancellationToken)
    {
        if ((LifecycleState)Volatile.Read(ref _state) is LifecycleState.Disposing or LifecycleState.Disposed)
        {
            return;
        }

        await _restartGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _ApplyTopologyChangeAsync().ConfigureAwait(false);
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

    /// <summary>
    /// Rebuilds the subscription groups whose shape differs from the running ones, under the restart gate. A group's
    /// shape is its concurrency and its consumers, so it changes when a runtime subscription joins or leaves it.
    /// </summary>
    private async ValueTask _ApplyTopologyChangeAsync()
    {
        if ((LifecycleState)Volatile.Read(ref _state) != LifecycleState.Running)
        {
            return;
        }

        var desired = _selector
            .GetCandidatesBySubscription()
            .ToDictionary(
                group => _CreateHandleName(group.Key),
                group => (Group: group, Shape: _GetGroupShape(group.Key, group.Value)),
                StringComparer.Ordinal
            );

        var stale = _subscriptionHandles
            .Where(handle =>
                !desired.TryGetValue(handle.Key, out var wanted)
                || !string.Equals(wanted.Shape, handle.Value.Shape, StringComparison.Ordinal)
            )
            .ToArray();
        var added = desired
            .Where(wanted =>
                !_subscriptionHandles.TryGetValue(wanted.Key, out var handle)
                || !string.Equals(wanted.Value.Shape, handle.Shape, StringComparison.Ordinal)
            )
            .Select(static wanted => wanted.Value.Group)
            .ToArray();

        if (stale.Length == 0 && added.Length == 0)
        {
            return;
        }

        // Circuit state outlives the clients, as it does on a full rebuild: a consumer that returns keeps its history.
        if (
            !await _RetireHandlesAsync([.. stale.Select(static x => x.Value)], _RestartShutdownTimeout)
                .ConfigureAwait(false)
        )
        {
            _isHealthy = false;
            _logger.ProcessorStopFailed(
                new TimeoutException("The changed subscriptions did not stop before the topology deadline."),
                nameof(ConsumerRegister)
            );
            return;
        }

        foreach (var handle in stale)
        {
            _subscriptionHandles.TryRemove(handle);
        }

        _RegisterKnownCircuits(desired.Values.Select(static x => x.Group));

        try
        {
            // Nothing waits for the establishment hooks under the restart gate, for the reason a rebuild gives.
            _ = await _StartSubscriptionsAsync(added).ConfigureAwait(false);
            await _PauseSurvivorsOfOpenCircuitsAsync(added).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when ((LifecycleState)Volatile.Read(ref _state) is LifecycleState.Disposing or LifecycleState.Disposed)
        {
            // Final shutdown owns the handles that were published before it won.
        }
        catch
        {
            // A group that failed to start leaves the host short of a subscription; the health watchdog's full
            // rebuild recovers it.
            _isHealthy = false;
            throw;
        }
    }

    /// <summary>
    /// Starting a group aborts any half-open probe of the circuits it delivers to, which reopens them without the pause
    /// callback. A full rebuild pre-pauses every new handle, so nothing else is affected; after a partial one, a group
    /// that kept running and shares such a circuit would stay resumed while it is Open, so it is paused here too.
    /// </summary>
    private async ValueTask _PauseSurvivorsOfOpenCircuitsAsync(
        KeyValuePair<ConsumerSubscriptionKey, IReadOnlyList<ConsumerExecutorDescriptor>>[] started
    )
    {
        if (_circuitBreakerStateManager is null || started.Length == 0)
        {
            return;
        }

        var startedNames = started.Select(static x => _CreateHandleName(x.Key)).ToHashSet(StringComparer.Ordinal);
        var circuitKeys = started
            .SelectMany(static x => x.Value)
            .Where(static x => !x.EveryInstance)
            .Select(CircuitBreakerKeys.For)
            .Distinct(StringComparer.Ordinal);

        foreach (var circuitKey in circuitKeys)
        {
            if (!_circuitBreakerStateManager.TryGetOpenEpoch(circuitKey, out var openEpoch))
            {
                continue;
            }

            foreach (var handle in _subscriptionHandles.Values)
            {
                if (
                    !startedNames.Contains(handle.SubscriptionName)
                    && handle.CircuitKeys.Contains(circuitKey)
                    && !handle.IsPauseAppliedForEpoch(openEpoch)
                )
                {
                    await _PauseSubscriptionAsync(handle, openEpoch).ConfigureAwait(false);
                }
            }
        }
    }

    /// <summary>
    /// The shape of one subscription group, compared ordinally to decide whether a topology change touches it: the
    /// concurrency its clients run with and every consumer they deliver to.
    /// </summary>
    private string _GetGroupShape(
        ConsumerSubscriptionKey subscriptionKey,
        IReadOnlyList<ConsumerExecutorDescriptor> descriptors
    )
    {
        var consumers = descriptors
            .Select(static x =>
                string.Join(
                    '\u001f',
                    x.MessageName,
                    x.ResolvedConsumerIdentity,
                    x.HandlerId,
                    x.ConsumerType?.AssemblyQualifiedName,
                    x.MessageType?.AssemblyQualifiedName,
                    x.MessageContractVersion
                )
            )
            .Order(StringComparer.Ordinal);

        return string.Join(
            '\u001e',
            consumers.Prepend(
                _selector.GetSubscriptionConcurrentLimit(subscriptionKey).ToString(CultureInfo.InvariantCulture)
            )
        );
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
                await quiesceTask.ConfigureAwait(false);
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
                _ = _ObserveBestEffortHandleTeardownAsync(_subscriptionHandles.Values.ToArray());
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

    private async Task _ObserveBestEffortHandleTeardownAsync(IReadOnlyCollection<SubscriptionHandle> handles)
    {
        try
        {
            // SubscriptionHandle.DisposeAsync is idempotent via its cached dispose task; TimeSpan.Zero bounds
            // each client ShutdownAsync to its immediate path so shutdown is at least initiated.
            await Task.WhenAll(handles.Select(handle => handle.DisposeAsync(TimeSpan.Zero).AsTask()))
                .ConfigureAwait(false);

            await _FinalizePulseAsync(handles, removeCircuitState: true, _stoppingCtsRegistration, _stoppingCts)
                .ConfigureAwait(false);

            _subscriptionHandles.Clear();
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
        var handles = _subscriptionHandles.Values.ToArray();

        if (!await _RetireHandlesAsync(handles, shutdownTimeout, shutdownStarted).ConfigureAwait(false))
        {
            return false;
        }

        var finalizationTask = _FinalizePulseAsync(handles, removeCircuitState, _stoppingCtsRegistration, _stoppingCts);
        if (
            !await _WaitWithinShutdownBudgetAsync(finalizationTask, shutdownStarted, shutdownTimeout)
                .ConfigureAwait(false)
        )
        {
            return false;
        }

        _subscriptionHandles.Clear();
        return true;
    }

    /// <summary>
    /// Stops the clients of <paramref name="handles"/> within one budget: cancels every subscription concurrently,
    /// waits for their consumer tasks, then disposes them. Returns <see langword="false"/> when the budget expires.
    /// </summary>
    private async Task<bool> _RetireHandlesAsync(
        IReadOnlyCollection<SubscriptionHandle> handles,
        TimeSpan shutdownTimeout,
        long? started = null
    )
    {
        if (handles.Count == 0)
        {
            return true;
        }

        var shutdownStarted = started ?? _timeProvider.GetTimestamp();

        // Signal every subscription concurrently so one slow cancellation callback cannot delay the others.
        var cancellationTask = Task.WhenAll(handles.Select(handle => handle.CancelAsync().AsTask()));
        if (
            !await _WaitWithinShutdownBudgetAsync(cancellationTask, shutdownStarted, shutdownTimeout)
                .ConfigureAwait(false)
        )
        {
            return false;
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
#pragma warning disable ERP022 // Listener cancellation/failure must not prevent client cleanup.
            catch (Exception)
            {
                // ignored
            }
#pragma warning restore ERP022
        }

        // Circuit state is not touched here: only final teardown removes it, because it must survive a rebuild.
        var remaining = _GetRemainingTimeout(shutdownStarted, shutdownTimeout);
        var disposalTask = Task.WhenAll(handles.Select(handle => handle.DisposeAsync(remaining).AsTask()));
        return await _WaitWithinShutdownBudgetAsync(disposalTask, shutdownStarted, shutdownTimeout)
            .ConfigureAwait(false);
    }

    private async Task _FinalizePulseAsync(
        IReadOnlyCollection<SubscriptionHandle> handles,
        bool removeCircuitState,
        CancellationTokenRegistration stoppingRegistration,
        CancellationTokenSource stoppingCts
    )
    {
        if (removeCircuitState && _circuitBreakerStateManager is not null)
        {
            await Task.WhenAll(
                    handles
                        .SelectMany(static handle => handle.CircuitKeys)
                        .Distinct(StringComparer.Ordinal)
                        .Select(circuitKey => _circuitBreakerStateManager.RemoveConsumerAsync(circuitKey).AsTask())
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

    /// <summary>
    /// Starts a client generation for every subscription and returns once each client receives, with the establishment
    /// hooks that generation raised. The hooks run on their own and never fault; host startup waits for them.
    /// </summary>
    private ValueTask<IReadOnlyCollection<Task>> _StartSubscriptionsAsync()
    {
        var subscriptions = _selector.GetCandidatesBySubscription();
        _RegisterKnownCircuits(subscriptions);
        return _StartSubscriptionsAsync([.. subscriptions]);
    }

    /// <summary>
    /// Circuits belong to consumer identities, not to the subscriptions their clients consume, so an identity whose
    /// messages arrive through several clients trips once and pauses all of them. Arming the known set also stops
    /// unrecognized keys from reaching the OTel cardinality. Every-instance consumers have no circuit: their deliveries
    /// are at most once, so there is no retry backlog for a breaker to protect.
    /// </summary>
    private void _RegisterKnownCircuits(
        IEnumerable<KeyValuePair<ConsumerSubscriptionKey, IReadOnlyList<ConsumerExecutorDescriptor>>> subscriptions
    )
    {
        _circuitBreakerStateManager?.RegisterKnownConsumers(
            subscriptions
                .SelectMany(static x => x.Value)
                .Where(static x => !x.EveryInstance)
                .Select(CircuitBreakerKeys.For)
                .Distinct(StringComparer.Ordinal)
        );
    }

    /// <summary>
    /// Starts a client generation for each of <paramref name="subscriptions"/> and returns once each client receives,
    /// with the establishment hooks that generation raised.
    /// </summary>
    private async ValueTask<IReadOnlyCollection<Task>> _StartSubscriptionsAsync(
        IReadOnlyCollection<
            KeyValuePair<ConsumerSubscriptionKey, IReadOnlyList<ConsumerExecutorDescriptor>>
        > subscriptions
    )
    {
        List<Task>? startupTasks = null;
        var establishments = new ConcurrentQueue<Task>();

        foreach (var match in subscriptions)
        {
            var subscriptionKey = match.Key;
            var handleName = _CreateHandleName(subscriptionKey);
            var limit = _selector.GetSubscriptionConcurrentLimit(subscriptionKey);
            var everyInstance = subscriptionKey.Kind is ConsumerSubscriptionKind.EveryInstance;
            var descriptors = match.Value;

            ICollection<string> messageNames;
            try
            {
                await using var client = await _CreateConsumerClientAsync(subscriptionKey, limit, _stoppingCts.Token)
                    .ConfigureAwait(false);
                client.AttachCallbacks(onMessage: null, onLog: _WriteLog);
                messageNames = await client
                    .FetchMessageNamesAsync(match.Value.Select(x => x.MessageName), _stoppingCts.Token)
                    .ConfigureAwait(false);
            }
            catch (BrokerConnectionException e)
            {
                _isHealthy = false;
                _logger.FailedToConnectToBroker(e);
                return establishments;
            }

            var groupCts = CancellationTokenSource.CreateLinkedTokenSource(_stoppingCts.Token);
            var handle = new SubscriptionHandle
            {
                Logger = _logger,
                Cts = groupCts,
                SubscriptionName = handleName,
                Shape = _GetGroupShape(subscriptionKey, descriptors),
                CircuitKeys = everyInstance
                    ? []
                    : match.Value.Select(CircuitBreakerKeys.For).ToFrozenSet(StringComparer.Ordinal),
            };

            _subscriptionHandles[handleName] = handle;

            if (_circuitBreakerStateManager is not null)
            {
                foreach (var circuitKey in handle.CircuitKeys)
                {
                    // Re-registering on restart replaces the previous generation's callbacks; they resolve the handles
                    // at call time, so they always reach the current clients.
                    _circuitBreakerStateManager.RegisterConsumerCallbacks(
                        circuitKey,
                        onPause: epoch => _ApplyCircuitIntentAsync(circuitKey, pause: true, epoch),
                        onResume: epoch => _ApplyCircuitIntentAsync(circuitKey, pause: false, epoch)
                    );

                    // Normalize HalfOpen → Open: the aborted probe is invalid on rebuilt transport clients.
                    // This is a no-op during initial startup (no circuits are in HalfOpen then).
                    await _circuitBreakerStateManager.AbortHalfOpenProbeAsync(circuitKey).ConfigureAwait(false);

                    // If the circuit is Open (or was just re-normalized from HalfOpen),
                    // pre-pause the new handle so newly created clients get paused via AddClientAsync.
                    if (_circuitBreakerStateManager.TryGetOpenEpoch(circuitKey, out var openEpoch))
                    {
                        await _PauseSubscriptionAsync(handle, openEpoch).ConfigureAwait(false);
                    }
                }
            }

            // An every-instance subscription belongs to this process, so a second client would open a second
            // subscription and deliver every message twice here; its concurrency still applies inside the one client.
            var clientCount = everyInstance ? 1 : _options.ConsumerThreadCount;

            for (var i = 0; i < clientCount; i++)
            {
                var startupReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var task = Task
                    .Factory.StartNew(
                        async () =>
                        {
                            try
                            {
                                var innerClient = await _CreateConsumerClientAsync(
                                        subscriptionKey,
                                        limit,
                                        groupCts.Token
                                    )
                                    .ConfigureAwait(false);

                                await handle.AddClientAsync(innerClient).ConfigureAwait(false);

                                _serverAddress = innerClient.BrokerAddress;

                                _RegisterMessageProcessor(innerClient, subscriptionKey, handle, groupCts.Token);

                                Action? onReady = null;
                                if (everyInstance)
                                {
                                    onReady = () =>
                                        establishments.Enqueue(
                                            _RaiseSubscriptionEstablished(handleName, descriptors, groupCts.Token)
                                        );

                                    // A transport that recovers the subscription on its own reports it here, so the
                                    // consumer learns about a gap the core never saw.
                                    innerClient.AttachReestablishedCallback(_ =>
                                        _RaiseSubscriptionEstablished(handleName, descriptors, groupCts.Token)
                                    );
                                }

                                await innerClient.SubscribeAsync(messageNames, groupCts.Token).ConfigureAwait(false);
                                await _AwaitConsumerReadyThenListenAsync(
                                        innerClient,
                                        startupReady,
                                        onReady,
                                        groupCts.Token
                                    )
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

        return establishments;
    }

    private Task<IConsumerClient> _CreateConsumerClientAsync(
        ConsumerSubscriptionKey subscriptionKey,
        byte groupConcurrent,
        CancellationToken cancellationToken
    )
    {
        return _consumerClientFactory.CreateAsync(
            new ConsumerClientRequest(
                subscriptionKey.SubscriptionName,
                groupConcurrent,
                subscriptionKey.Lane,
                subscriptionKey.Kind,
                _instanceId
            ),
            cancellationToken
        );
    }

    // Names the clients of one subscription on one lane; circuits are keyed by consumer identity instead. The kind is
    // part of the name because a competing and an every-instance subscription never share clients.
    private static string _CreateHandleName(ConsumerSubscriptionKey subscriptionKey)
    {
        return subscriptionKey.Kind is ConsumerSubscriptionKind.EveryInstance
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{(short)subscriptionKey.Lane}:{subscriptionKey.SubscriptionName}:every-instance"
            )
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{(short)subscriptionKey.Lane}:{subscriptionKey.SubscriptionName}"
            );
    }

    private async Task _AwaitConsumerReadyThenListenAsync(
        IConsumerClient innerClient,
        TaskCompletionSource startupReady,
        Action? onReady,
        CancellationToken cancellationToken
    )
    {
        var readinessTask = innerClient.WaitUntilReadyAsync(cancellationToken).AsTask();

        if (readinessTask.IsCompleted)
        {
            await readinessTask.ConfigureAwait(false);
            onReady?.Invoke();
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

        // The hook is raised once the subscription receives and runs beside the listening client, so no message
        // published after establishment is lost to it. The listening loop never waits for it: a hook that restarts the
        // clients would otherwise wait on its own consumer task.
        onReady?.Invoke();
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
            // Nothing waits for the establishment hooks under the restart gate: a hook that attaches a runtime
            // subscription restarts the clients again, and that restart needs the gate this one holds.
            _ = await _StartSubscriptionsAsync().ConfigureAwait(false);
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
            await _ApplyTopologyChangeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Applies a circuit's pause or resume to every client that consumes the circuit's identity. A client shared by
    /// several identities resumes only once none of its other circuits is still Open, so one identity recovering never
    /// releases deliveries of another that is still tripped.
    /// </summary>
    private async ValueTask _ApplyCircuitIntentAsync(string circuitKey, bool pause, long epoch)
    {
        foreach (var handle in _subscriptionHandles.Values)
        {
            if (!handle.CircuitKeys.Contains(circuitKey))
            {
                continue;
            }

            if (!pause && _IsAnotherCircuitOpen(handle, circuitKey))
            {
                continue;
            }

            await _ApplySubscriptionIntentAsync(handle, pause, epoch).ConfigureAwait(false);
        }
    }

    private bool _IsAnotherCircuitOpen(SubscriptionHandle handle, string circuitKey)
    {
        foreach (var other in handle.CircuitKeys)
        {
            if (
                !string.Equals(other, circuitKey, StringComparison.Ordinal)
                && _circuitBreakerStateManager?.GetState(other) is CircuitBreakerState.Open
            )
            {
                return true;
            }
        }

        return false;
    }

    private ValueTask _PauseSubscriptionAsync(SubscriptionHandle handle, long epoch)
    {
        return _ApplySubscriptionIntentAsync(handle, pause: true, epoch);
    }

    private async ValueTask _ApplySubscriptionIntentAsync(SubscriptionHandle handle, bool pause, long epoch)
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
                _logger.StaleCircuitIntentSkipped(handle.SubscriptionName, epoch, handle.LastAppliedEpoch);
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

    private async ValueTask _PauseClientsAsync(SubscriptionHandle handle)
    {
        _logger.CircuitBreakerOpenedPausingConsumers(handle.SubscriptionName);

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
                        _logger.PauseConsumerClientFailed(ex, handle.SubscriptionName);
                    }
                })
            )
            .ConfigureAwait(false);
    }

    private async ValueTask _ResumeClientsAsync(SubscriptionHandle handle)
    {
        _logger.ResumingConsumersHalfOpen(handle.SubscriptionName);

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
                        _logger.ResumeConsumerClientFailed(ex, handle.SubscriptionName);
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
            $"Failed to resume one or more consumer clients for subscription '{handle.SubscriptionName}'.",
            failureList
        );
    }

    private void _RegisterMessageProcessor(
        IConsumerClient client,
        ConsumerSubscriptionKey subscriptionKey,
        SubscriptionHandle clientHandle,
        CancellationToken hostShutdownToken
    )
    {
        if (subscriptionKey.Kind is ConsumerSubscriptionKind.EveryInstance)
        {
            client.AttachCallbacks(
                (transportMessage, sender) =>
                    _OnEveryInstanceMessageAsync(client, subscriptionKey, transportMessage, sender, hostShutdownToken),
                _WriteLog
            );

            return;
        }

        client.AttachCallbacks(
            (transportMessage, sender) =>
                _OnCompetingMessageAsync(
                    client,
                    subscriptionKey,
                    clientHandle,
                    transportMessage,
                    sender,
                    hostShutdownToken
                ),
            _WriteLog
        );
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

    private sealed class SubscriptionHandle
    {
        private readonly Lock _clientsLock = new();
        private Task? _disposeTask;
        private bool _disposing;
        private bool _isPaused;

        // SemaphoreSlim.Dispose never completes queued waiters and breaks the holder's release.
        // This handle-generation gate is intentionally left undisposed; disposal is signalled by
        // _disposing and checked before waiting and after acquiring the gate.
        public SemaphoreSlim ApplyGate { get; } = new(1, 1);

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
        public required string SubscriptionName { get; init; }

        /// <summary>The group shape the clients were started for; a topology change rebuilds the handle when it differs.</summary>
        public required string Shape { get; init; }

        /// <summary>The lane-qualified circuit keys of the consumer identities this handle's clients deliver to.</summary>
        public FrozenSet<string> CircuitKeys { get; init; } = [];
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
                    Logger.LogPauseNewlyAddedClientFailed(ex, SubscriptionName);
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
        Message = "Failed to pause newly added consumer client for subscription '{SubscriptionName}'."
    )]
    public static partial void LogPauseNewlyAddedClientFailed(
        this ILogger logger,
        Exception exception,
        string subscriptionName
    );
}
