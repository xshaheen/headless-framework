// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Coordination;
using Headless.DistributedLocks;
using Headless.Messaging.RequestReply;
using Headless.Messaging.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Internal;

/// <summary>Default implement of <see cref="IBootstrapper" />.</summary>
internal sealed class Bootstrapper(
    IServiceProvider serviceProvider,
    IOptions<MessagingOptions> options,
    ILogger<IBootstrapper> logger
) : BackgroundService, IBootstrapper
{
    private readonly Lock _bootstrapLock = new();
    private readonly TimeProvider _timeProvider = serviceProvider.GetService<TimeProvider>() ?? TimeProvider.System;
    private IReadOnlyList<IProcessingServer> _processors = [];
    private bool _disposed;
    private bool _isStopping;
    private CancellationTokenSource? _runtimeCts;
    private Task? _bootstrapTask;
    private Task? _shutdownTask;

    // Plain access under _bootstrapLock (the lock provides a full fence).
    // Volatile.Read in IsStarted for lock-free snapshot by external callers.
    private bool _isStarted;

    public bool IsStarted => Volatile.Read(ref _isStarted);

    public async Task BootstrapAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Task bootstrapTask;
        var createdByCaller = false;

        lock (_bootstrapLock)
        {
            Ensure.NotDisposed(_disposed, this);

            if (_isStopping)
            {
                throw new InvalidOperationException("Cannot bootstrap after shutdown has begun.");
            }

            if (_isStarted)
            {
                logger.MessagingAlreadyStarted();
                return;
            }

            if (_bootstrapTask is not null)
            {
                logger.MessagingAlreadyStarted();
                bootstrapTask = _bootstrapTask;
            }
            else
            {
                logger.MessagingStarting();

                var runtimeCts = new CancellationTokenSource();
                _runtimeCts = runtimeCts;
                bootstrapTask = _BootstrapAsyncCore(runtimeCts, cancellationToken);
                _bootstrapTask = bootstrapTask;
                createdByCaller = true;
            }
        }

        if (createdByCaller)
        {
            await bootstrapTask.ConfigureAwait(false);
        }
        else
        {
            await bootstrapTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task _BootstrapAsyncCore(CancellationTokenSource runtimeCts, CancellationToken ownerCancellationToken)
    {
        using var startupCts = CancellationTokenSource.CreateLinkedTokenSource(
            runtimeCts.Token,
            ownerCancellationToken
        );
        var startupToken = startupCts.Token;

        try
        {
            _CheckRequirement();
            _WarnIfNoOpProvider();
            _WarnIfNullNodeMembership();
            _WarnIfDispatchTimeoutMateriallyExceedsInitialGrace();

            // Publish before any processor start can block, so shutdown always reaches every
            // processor. Resolution stays outside the lock: processor factories are third-party code
            // that can block or throw, and every other _bootstrapLock holder would wait behind them.
            IProcessingServer[] resolvedProcessors = [.. serviceProvider.GetServices<IProcessingServer>()];

            // The reply listener starts first, so a consumer that sends a request while handling its first message
            // finds a reply channel already open.
            if (serviceProvider.GetService<ReplyListenerHost>() is { } replyListener)
            {
                resolvedProcessors = [replyListener, .. resolvedProcessors];
            }

            lock (_bootstrapLock)
            {
                _processors = resolvedProcessors;
            }

            // The primary storage's schema is not created here: a relational provider contributes its DDL to the Headless
            // schema runner, which applies it in IHostedLifecycleService.StartingAsync, before any hosted service (this
            // BackgroundService included) starts. An invalid AddOutbox() registration fails here, before outbox work.
            var secondaries = serviceProvider.GetService<MessagingOutboxes>()?.Secondaries ?? [];

            // An additional outbox serves only units of work on its own database, and its schema is applied by its own
            // runner: one module's database being down must not stop the host, so its failure is logged and the outbox
            // initialization processor retries it.
            await Task.WhenAll(secondaries.Select(outbox => _TryInitializeOutboxAsync(outbox, startupToken)))
                .ConfigureAwait(false);

            if (_IsShutdownStarted())
            {
                return;
            }

            await _BootstrapCoreAsync(startupToken).ConfigureAwait(false);

            var wasStopping = false;

            lock (_bootstrapLock)
            {
                if (_isStopping)
                {
                    // Shutdown began while we were starting — undo immediately.
                    _bootstrapTask = null;
                    _isStarted = false;
                    wasStopping = true;
                }
                else
                {
                    _isStarted = true;
                    _bootstrapTask = null;
                }
            }

            if (wasStopping)
            {
                return;
            }

            logger.MessagingStarted();
        }
        catch
        {
            try
            {
                await runtimeCts.CancelAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                // Ignore races with external disposal during startup failure cleanup.
            }

            bool shutdownOwnsCleanup;

            lock (_bootstrapLock)
            {
                shutdownOwnsCleanup = _isStopping;

                if (ReferenceEquals(_runtimeCts, runtimeCts))
                {
                    _runtimeCts = null;
                }

                _bootstrapTask = null;
                _isStarted = false;
            }

            if (!shutdownOwnsCleanup)
            {
                await _StopProcessorsAsync().ConfigureAwait(false);
                runtimeCts.Dispose();
            }

            throw;
        }
    }

    private async Task _TryInitializeOutboxAsync(MessagingOutbox outbox, CancellationToken cancellationToken)
    {
        try
        {
            await outbox.EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.OutboxStorageInitFailed(e, outbox.Name);
        }
    }

    private async Task _BootstrapCoreAsync(CancellationToken cancellationToken)
    {
        List<Exception>? failures = null;

        foreach (var item in _processors)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                await item.StartAsync(cancellationToken).ConfigureAwait(false);

                if (_IsShutdownStarted())
                {
                    _QuiesceLateStartedProcessor(item);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.ProcessorsStartedError(ex);
                failures ??= [];
                failures.Add(ex);
            }
        }

        if (failures is { Count: > 0 })
        {
            if (failures.Count == 1)
            {
                throw failures[0];
            }

            throw new AggregateException("One or more messaging processors failed to start.", failures);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await BootstrapAsync(stoppingToken).ConfigureAwait(false);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await _StartShutdown(dispose: false).WaitAsync(cancellationToken).ConfigureAwait(false);
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    private void _WarnIfNoOpProvider()
    {
        if (!options.Value.UseStorageLock)
        {
            return;
        }

        var lockProvider = serviceProvider.GetRequiredKeyedService<IDistributedLock>(MessagingKeys.LockProvider);

        // Direct type check on the public sentinel — sealed, so the test is exact. A user
        // who deliberately wraps a NullDistributedLock in a decorator will bypass
        // this warning; that's an opt-out we accept rather than guard against, since the
        // sentinel exists specifically to be detectable.
        if (lockProvider is not NullDistributedLock)
        {
            return;
        }

        // Probe the un-keyed slot so the warning can distinguish the "no provider at all" case
        // from the "real provider registered but only un-keyed" case — the second case is a
        // common misconfiguration where the operator wired up Headless.DistributedLocks.Redis
        // (or similar) but did not flow it through MessagingBuilder.UseDistributedLock(...).
        //
        // Probe is purely informational; wrapping in try/catch ensures a misconfigured un-keyed
        // factory (e.g., missing Redis connection string) cannot fail messaging bootstrap. On
        // probe failure we fall through to the conservative "no provider" EventId 77 — the
        // factory's real error will surface at first lock acquisition with a clearer message.
        IDistributedLock? unkeyedProvider = null;
        try
        {
            unkeyedProvider = serviceProvider.GetService<IDistributedLock>();
        }
#pragma warning disable ERP022 // Intentional: probe failure must not block startup. EventId 77 fallback emits below.
        catch (Exception)
        {
            // Intentional: probe failure must not block startup. EventId 77 fallback emits below.
        }
#pragma warning restore ERP022

        if (unkeyedProvider is not null and not NullDistributedLock)
        {
            logger.UseStorageLockWithNoOpProviderButRealUnkeyed();
            return;
        }

        logger.UseStorageLockWithNoOpProvider();
    }

    private void _WarnIfNullNodeMembership()
    {
        // Dead-owner recovery runs unconditionally via DeadOwnerRecoveryBridge, independent of UseStorageLock.
        // It can only accelerate recovery when a real INodeMembership reports dead nodes; with the
        // NullNodeMembership default the bridge is a benign no-op and recovery falls back to the per-row
        // LockedUntil lease floor.
        var membership = serviceProvider.GetService<INodeMembership>();
        if (membership is null or NullNodeMembership)
        {
            logger.MessagingRecoveryUsingLockedUntilFloorOnly();
            return;
        }

        // Recovery is active. Dead-only reclaim avoids duplicate delivery only when a node is classified Dead
        // no sooner than its in-flight dispatch could still be running — i.e. Coordination's DeadThreshold must
        // be >= the retry DispatchTimeout. Otherwise a still-alive node that crosses the dead threshold
        // mid-dispatch is reclaimed and its message re-dispatched. Warn rather than fail: a redundant delivery
        // is within the at-least-once contract, and the two thresholds live in separate option packages.
        var deadThreshold = serviceProvider.GetService<IOptions<CoordinationOptions>>()?.Value.DeadThreshold;
        var dispatchTimeout = options.Value.RetryPolicy.DispatchTimeout;
        if (deadThreshold is { } threshold && threshold < dispatchTimeout)
        {
            logger.MessagingDeadThresholdBelowDispatchTimeout(threshold, dispatchTimeout);
        }
    }

    private void _WarnIfDispatchTimeoutMateriallyExceedsInitialGrace()
    {
        var retryPolicy = options.Value.RetryPolicy;
        var threshold = TimeSpan.FromMinutes(2);
        var initialGrace = retryPolicy.InitialDispatchGrace;

        if (initialGrace > TimeSpan.MaxValue - threshold)
        {
            return;
        }

        if (retryPolicy.DispatchTimeout > initialGrace + threshold)
        {
            logger.MessagingDispatchTimeoutMateriallyExceedsInitialGrace(
                retryPolicy.DispatchTimeout,
                initialGrace,
                options.Value.ShutdownTimeout
            );
        }
    }

    private void _CheckRequirement()
    {
        _ =
            serviceProvider.GetService<MessagingMarkerService>()
            ?? throw new InvalidOperationException(
                "AddHeadlessMessaging() must be added on the service collection.   eg: services.AddHeadlessMessaging(...)"
            );

        _CheckMessageNameCollisions();
        _CheckReplyNamespaceIsFree();
        var capabilities = serviceProvider.GetRequiredService<MessagingCapabilityModel>();
        capabilities.ValidateRoutingAffinityStartup(
            serviceProvider
                .GetRequiredService<IMessageMetadataRegistry>()
                .GetAll()
                .Where(route => capabilities.Supports(route.Route.Lane, MessagingProviderRole.Transport))
        );
        var consumers = serviceProvider.GetRequiredService<ConsumerRegistry>().GetAll();
        var gate = serviceProvider.GetRequiredService<IMessageCapabilityGate>();

        // Every-instance consumers keep no inbox state, so they alone never require an inbox guarantee from storage.
        var hasDurableConsumers = consumers.Any(static consumer => !consumer.EveryInstance);
        gate.ValidateStartup(
            _GetRegisteredRoutes(capabilities),
            hasDurableConsumers,
            options.Value.MinimumInboxGuarantee
        );

        // Runs before any processor starts, so a transport without every-instance subscriptions rejects the consumer
        // before any consumer client, and so any broker object, exists.
        foreach (
            var identity in consumers
                .Where(static x => x.EveryInstance)
                .Select(static x => x.ConsumerIdentity)
                .Distinct(StringComparer.Ordinal)
        )
        {
            gate.EnsureEveryInstanceSupported(identity);
        }

        // Runs before the reply listener and any processor start, so a host that sends requests or answers them on a
        // transport without a reply channel fails before it reports ready, instead of timing out every call later.
        if (serviceProvider.GetService<RequestReplyMarkerService>() is not null)
        {
            gate.EnsureRequestReplySupported();
        }

        foreach (
            var identity in consumers
                .Where(static x => x.IsResponder)
                .Select(static x => x.ConsumerIdentity)
                .Distinct(StringComparer.Ordinal)
        )
        {
            gate.EnsureRequestReplySupported(identity);
        }
    }

    /// <summary>
    /// The routes startup validates against the transport and storage: every consumer's route, and each declared
    /// message name on the lanes the transport carries.
    /// </summary>
    /// <remarks>
    /// A message contract or name mapping names the message for both lanes, because it belongs to the message schema and
    /// a shared contracts package cannot know the host's transport. A lane the transport does not carry is left out, so a
    /// Queue-only transport accepts every contract; a consumer on that lane still fails here, and a publish on it fails
    /// when it is attempted.
    /// </remarks>
    private HashSet<MessageRouteKey> _GetRegisteredRoutes(MessagingCapabilityModel capabilities)
    {
        var routes = new HashSet<MessageRouteKey>();

        foreach (var (route, needsTransportLane) in _GetAllRoutes())
        {
            if (!needsTransportLane || capabilities.Supports(route.Lane, MessagingProviderRole.Transport))
            {
                routes.Add(route);
            }
        }

        return routes;
    }

    /// <summary>
    /// Every route the host names, in order: consumer routes, declared routes, then effective message-name routes.
    /// <c>NeedsTransportLane</c> marks a route that names a message for both lanes, and so counts only on a lane the
    /// transport carries.
    /// </summary>
    private IEnumerable<(MessageRouteKey Route, bool NeedsTransportLane)> _GetAllRoutes()
    {
        var registry = serviceProvider.GetRequiredService<ConsumerRegistry>();

        foreach (var consumer in registry.GetAll())
        {
            yield return (new MessageRouteKey(consumer.MessageType, consumer.MessageName, consumer.Lane), false);
        }

        foreach (var (registration, route) in _GetDeclaredRoutes(registry))
        {
            yield return (route, registration.DeclaresMessage);
        }

        foreach (var route in _GetEffectiveMessageNameRoutes(registry))
        {
            yield return (route, true);
        }
    }

    private IEnumerable<(MessageRegistration Registration, MessageRouteKey Route)> _GetDeclaredRoutes(
        ConsumerRegistry registry
    )
    {
        foreach (var registration in registry.DeclaredRoutes)
        {
            var rawName = registration.MessageName;
            if (
                rawName is null
                && !registry.TryGetRawMessageName(registration.MessageType, registration.Lane, out rawName)
            )
            {
                rawName = options.Value.Conventions.GetMessageName(registration.MessageType);
            }

            yield return (
                registration,
                new MessageRouteKey(
                    registration.MessageType,
                    options.Value.ApplyMessageNamePrefix(rawName),
                    registration.Lane
                )
            );
        }
    }

    /// <summary>
    /// Rejects any message name inside the reserved reply namespace, on either lane and whether or not the transport
    /// carries that lane.
    /// </summary>
    /// <remarks>
    /// A responder writes a reply to whatever address a request names once the address passes the namespace check, so
    /// a lane destination under <see cref="ReplyAddresses.Prefix"/> could receive forged replies. The check
    /// runs on the effective names, after <see cref="MessagingOptions.MessageNamePrefix"/>, because a prefix can move an
    /// otherwise harmless name into the namespace.
    /// </remarks>
    private void _CheckReplyNamespaceIsFree()
    {
        foreach (var (route, _) in _GetAllRoutes())
        {
            if (route.MessageName.StartsWith(ReplyAddresses.Prefix, StringComparison.Ordinal))
            {
                throw new MessagingConfigurationException(
                    $"Message name '{route.MessageName}' of {route.ContractType.FullName ?? route.ContractType.Name} on lane {route.Lane} is inside the reserved reply namespace '{ReplyAddresses.Prefix}'. Rename the message, or change {nameof(MessagingOptions.MessageNamePrefix)} if it adds the reserved prefix."
                );
            }
        }
    }

    private IEnumerable<MessageRouteKey> _GetEffectiveMessageNameRoutes(ConsumerRegistry registry)
    {
        var laneMappings = registry.GetLaneMessageNameMappings();

        foreach (var mapping in registry.GetMessageNameMappings())
        {
            var name = options.Value.ApplyMessageNamePrefix(mapping.Value);

            if (!laneMappings.ContainsKey((mapping.Key, MessageLane.Bus)))
            {
                yield return new MessageRouteKey(mapping.Key, name, MessageLane.Bus);
            }

            if (!laneMappings.ContainsKey((mapping.Key, MessageLane.Queue)))
            {
                yield return new MessageRouteKey(mapping.Key, name, MessageLane.Queue);
            }
        }

        foreach (var mapping in laneMappings)
        {
            yield return new MessageRouteKey(
                mapping.Key.MessageType,
                options.Value.ApplyMessageNamePrefix(mapping.Value),
                mapping.Key.Lane
            );
        }
    }

    private void _CheckMessageNameCollisions()
    {
        var registry = serviceProvider.GetRequiredService<ConsumerRegistry>();
        var consumers = registry.GetAll();
        var namesByLane = new Dictionary<MessageLane, Dictionary<string, HashSet<Type>>>
        {
            [MessageLane.Bus] = new(StringComparer.OrdinalIgnoreCase),
            [MessageLane.Queue] = new(StringComparer.OrdinalIgnoreCase),
        };

        foreach (var consumer in consumers)
        {
            _TrackMessageName(namesByLane, consumer.Lane, consumer.MessageName, consumer.MessageType);
        }

        foreach (var route in _GetEffectiveMessageNameRoutes(registry))
        {
            _TrackMessageName(namesByLane, route.Lane, route.MessageName, route.ContractType);
        }

        var (collisionLane, collisionName, types) = namesByLane
            .SelectMany(static lane => lane.Value.Select(name => (Lane: lane.Key, Name: name.Key, Types: name.Value)))
            .FirstOrDefault(static pair => pair.Types.Count > 1);

        if (types is null)
        {
            return;
        }

        var typeNames = types.Select(static type => type.FullName ?? type.Name).Order(StringComparer.Ordinal).ToArray();

        throw new InvalidOperationException(
            $"Message name '{collisionName}' on lane {collisionLane} is mapped to multiple message types: {string.Join(", ", typeNames)}."
        );
    }

    private static void _TrackMessageName(
        Dictionary<MessageLane, Dictionary<string, HashSet<Type>>> namesByLane,
        MessageLane lane,
        string messageName,
        Type messageType
    )
    {
        var nameToTypes = namesByLane[lane];
        if (!nameToTypes.TryGetValue(messageName, out var types))
        {
            types = [];
            nameToTypes[messageName] = types;
        }

        types.Add(messageType);
    }

    public override void Dispose()
    {
        _ = _StartShutdown(dispose: true);
        base.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await _StartShutdown(dispose: true).ConfigureAwait(false);
        base.Dispose();
    }

    private Task _StartShutdown(bool dispose)
    {
        TaskCompletionSource shutdownCompletion;
        Task? pendingBootstrap;
        CancellationTokenSource? runtimeCts;
        Task shutdownTask;
        long shutdownStarted;

        lock (_bootstrapLock)
        {
            if (dispose)
            {
                _disposed = true;
            }

            _isStopping = true;
            Volatile.Write(ref _isStarted, value: false);

            if (_shutdownTask is not null)
            {
                return _shutdownTask;
            }

            pendingBootstrap = _bootstrapTask;
            _bootstrapTask = null;
            runtimeCts = _runtimeCts;
            _runtimeCts = null;

            shutdownCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            shutdownTask = shutdownCompletion.Task;
            _shutdownTask = shutdownTask;
            shutdownStarted = _timeProvider.GetTimestamp();
        }

        _ = _RunShutdownAsync(pendingBootstrap, runtimeCts, shutdownStarted, shutdownCompletion);
        return shutdownTask;
    }

    private async Task _RunShutdownAsync(
        Task? pendingBootstrap,
        CancellationTokenSource? runtimeCts,
        long shutdownStarted,
        TaskCompletionSource shutdownCompletion
    )
    {
        var cleanupTask = _ShutdownCoreAsync(pendingBootstrap, runtimeCts, shutdownStarted);

        try
        {
            await cleanupTask
                .WaitAsync(_GetRemainingShutdownTime(shutdownStarted), _timeProvider, CancellationToken.None)
                .ConfigureAwait(false);
            shutdownCompletion.TrySetResult();
        }
        catch (TimeoutException)
        {
            _ = _ObserveCompletionAsync(cleanupTask);
            shutdownCompletion.TrySetResult();
        }
        catch (Exception ex)
        {
            shutdownCompletion.TrySetException(ex);
        }
    }

    private async Task _ShutdownCoreAsync(
        Task? pendingBootstrap,
        CancellationTokenSource? runtimeCts,
        long shutdownStarted
    )
    {
        // Phase 1 reaches the complete processor set before awaiting bootstrap, cancellation
        // callbacks, or any processor drain. Third-party DisposeAsync entry is isolated because
        // it is the only stop signal their public contract exposes and may block synchronously.
        var processorStops = _StopProcessorsAsync(shutdownStarted);
        var runtimeCancellation = _CancelRuntimeAsync(runtimeCts);
        var bootstrapObservation = _ObserveCompletionAsync(pendingBootstrap);

        try
        {
            await Task.WhenAll(processorStops, runtimeCancellation, bootstrapObservation).ConfigureAwait(false);
        }
        finally
        {
            runtimeCts?.Dispose();
        }
    }

    private static async Task _CancelRuntimeAsync(CancellationTokenSource? runtimeCts)
    {
        if (runtimeCts is null)
        {
            return;
        }

        try
        {
            await runtimeCts.CancelAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // Startup failure cleanup may have won the race and disposed the source.
        }
    }

    private static async Task _ObserveCompletionAsync(Task? task)
    {
        if (task is null)
        {
            return;
        }

#pragma warning disable ERP022 // Shutdown preserves source outcomes while preventing unobserved cleanup failures.
        try
        {
            await task.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        // ReSharper disable once EmptyGeneralCatchClause
        catch { }
#pragma warning restore ERP022
    }

    private Task _StopProcessorsAsync()
    {
        return _StopProcessorsAsync(_timeProvider.GetTimestamp());
    }

    private async Task _StopProcessorsAsync(long shutdownStarted)
    {
        logger.MessagingStopping();

        // Snapshot under the publisher's lock so a shutdown racing bootstrap sees the full set.
        IReadOnlyList<IProcessingServer> publishedProcessors;
        lock (_bootstrapLock)
        {
            publishedProcessors = _processors;
        }

        var processors = publishedProcessors.Reverse().ToArray();
        var stopTasks = new List<(IProcessingServer Processor, Task Task)>(processors.Length);
        var thirdPartyInitiated = new List<Task>();

        // Phase 1a: quiesce every processor that exposes the split shutdown capability.
        foreach (var item in processors)
        {
            if (item is IProcessingServerShutdown bounded)
            {
                try
                {
                    bounded.Quiesce();
                }
                catch (Exception ex)
                {
                    stopTasks.Add((item, Task.FromException(ex)));
                }
            }
        }

        // Phase 1b: third-party processors expose only combined disposal. Isolate each call after
        // every split-capable processor is quiesced, then confirm every call was entered before drain.
        foreach (var item in processors)
        {
            if (item is IProcessingServerShutdown)
            {
                continue;
            }

            var initiation = _InitiateThirdPartyStop(item);
            stopTasks.Add((item, initiation.StopTask));
            thirdPartyInitiated.Add(initiation.Initiated);
        }

        if (thirdPartyInitiated.Count > 0)
        {
            await Task.WhenAll(thirdPartyInitiated).ConfigureAwait(false);
        }

        // Phase 2: all built-ins now share the one monotonic remaining deadline. Initiate every
        // drain before awaiting any one processor so a blocked first processor cannot starve later ones.
        foreach (var item in processors)
        {
            if (item is not IProcessingServerShutdown bounded)
            {
                continue;
            }

            try
            {
                stopTasks.Add((item, bounded.StopAsync(_GetRemainingShutdownTime(shutdownStarted)).AsTask()));
            }
            catch (Exception ex)
            {
                stopTasks.Add((item, Task.FromException(ex)));
            }
        }

        var outcomes = await Task.WhenAll(
                stopTasks.Select(pair => _ObserveProcessorStopAsync(pair.Processor, pair.Task))
            )
            .ConfigureAwait(false);

        // Awaiting Task.WhenAll surfaces only the first fault; aggregate explicitly so callers see every
        // processor that failed to stop, mirroring the start-failure path.
        var failures = outcomes.OfType<Exception>().ToList();
        if (failures.Count > 0)
        {
            throw new AggregateException("One or more messaging processors failed to stop cleanly.", failures);
        }
    }

    private static ThirdPartyStopInitiation _InitiateThirdPartyStop(IProcessingServer processor)
    {
        var initiated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // A third-party DisposeAsync may block before returning its ValueTask. Use a dedicated worker
        // so thread-pool congestion cannot prevent phase 1 from reaching it and starve every built-in drain.
        var stopTask = Task
            .Factory.StartNew(
                async () =>
                {
                    initiated.TrySetResult();
                    await processor.DisposeAsync().ConfigureAwait(false);
                },
                CancellationToken.None,
                TaskCreationOptions.DenyChildAttach | TaskCreationOptions.LongRunning,
                TaskScheduler.Default
            )
            .Unwrap();
        return new ThirdPartyStopInitiation(stopTask, initiated.Task);
    }

    /// <summary>Observes one processor stop; returns the failure (null on success or expected cancellation).</summary>
    private async Task<Exception?> _ObserveProcessorStopAsync(IProcessingServer processor, Task stopTask)
    {
        try
        {
            await stopTask.ConfigureAwait(false);
            return null;
        }
        catch (OperationCanceledException ex)
        {
            logger.ExpectedOperationCanceledException(ex, ex.Message);
            return null;
        }
        catch (Exception ex)
        {
            logger.ProcessorStopFailed(ex, processor.GetType().FullName ?? processor.GetType().Name);
            return ex;
        }
    }

    private TimeSpan _GetRemainingShutdownTime(long shutdownStarted)
    {
        // Read at use time rather than caching at bootstrap: one less field whose staleness a
        // future refactor could expose.
        var remaining = options.Value.ShutdownTimeout - _timeProvider.GetElapsedTime(shutdownStarted);
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    private bool _IsShutdownStarted()
    {
        lock (_bootstrapLock)
        {
            return _isStopping;
        }
    }

    private void _QuiesceLateStartedProcessor(IProcessingServer processor)
    {
        try
        {
            Task stopTask;
            if (processor is IProcessingServerShutdown bounded)
            {
                bounded.Quiesce();
                stopTask = bounded.StopAsync(TimeSpan.Zero).AsTask();
            }
            else
            {
                stopTask = _InitiateThirdPartyStop(processor).StopTask;
            }

            _ = _ObserveCompletionAsync(stopTask);
        }
        catch (Exception ex)
        {
            logger.ProcessorStopFailed(ex, processor.GetType().FullName ?? processor.GetType().Name);
        }
    }

    private readonly record struct ThirdPartyStopInitiation(Task StopTask, Task Initiated);
}
