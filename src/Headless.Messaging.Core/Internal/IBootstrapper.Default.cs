// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.DistributedLocks;
using Headless.Messaging.Configuration;
using Headless.Messaging.Persistence;
using Headless.Messaging.Registration;
using Headless.Messaging.Runtime;
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
            ObjectDisposedException.ThrowIf(_disposed, typeof(Bootstrapper));

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

            try
            {
                var storageInitializer = serviceProvider.GetRequiredService<IStorageInitializer>();
                await storageInitializer.InitializeAsync(startupToken).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not InvalidOperationException)
            {
                logger.StorageInitFailed(e);
                throw;
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

    private async Task _BootstrapCoreAsync(CancellationToken cancellationToken)
    {
        List<Exception>? failures = null;
        _processors = serviceProvider.GetServices<IProcessingServer>().ToArray();

        foreach (var item in _processors)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                await item.StartAsync(cancellationToken).ConfigureAwait(false);
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
#pragma warning disable RCS1075, ERP022 // Intentional: probe failure must not block startup. EventId 77 fallback emits below.
        catch (Exception)
        {
            // Intentional: probe failure must not block startup. EventId 77 fallback emits below.
        }
#pragma warning restore RCS1075, ERP022

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

        _DrainPendingMessageRegistrations();
        _CheckMessageNameCollisions();
        serviceProvider.GetRequiredService<IMessageCapabilityGate>().ValidateStartup(_GetRegisteredRoutes());
    }

    private HashSet<MessageRouteKey> _GetRegisteredRoutes()
    {
        var registry = serviceProvider.GetRequiredService<ConsumerRegistry>();
        var routes = registry
            .GetAll()
            .Select(static consumer => new MessageRouteKey(consumer.MessageType, consumer.MessageName, consumer.Lane))
            .ToHashSet();

        foreach (var registration in serviceProvider.GetServices<MessageRegistration>())
        {
            var rawName = registration.MessageName;
            if (
                rawName is null
                && !registry.TryGetRawMessageName(registration.MessageType, registration.Lane, out rawName)
            )
            {
                rawName = options.Value.Conventions.GetMessageName(registration.MessageType);
            }

            routes.Add(
                new MessageRouteKey(
                    registration.MessageType,
                    options.Value.ApplyMessageNamePrefix(rawName),
                    registration.Lane
                )
            );
        }

        foreach (var route in _GetEffectiveMessageNameRoutes(registry))
        {
            routes.Add(route);
        }

        return routes;
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
        }

        _ = _RunShutdownAsync(pendingBootstrap, runtimeCts, shutdownCompletion);
        return shutdownTask;
    }

    private async Task _RunShutdownAsync(
        Task? pendingBootstrap,
        CancellationTokenSource? runtimeCts,
        TaskCompletionSource shutdownCompletion
    )
    {
        var cleanupTask = _ShutdownCoreAsync(pendingBootstrap, runtimeCts);

        try
        {
            await cleanupTask
                .WaitAsync(options.Value.ShutdownTimeout, _timeProvider, CancellationToken.None)
                .ConfigureAwait(false);
            shutdownCompletion.TrySetResult();
        }
        catch (TimeoutException)
        {
            _ = _ObserveEventualShutdownAsync(cleanupTask);
            shutdownCompletion.TrySetResult();
        }
        catch (Exception ex)
        {
            shutdownCompletion.TrySetException(ex);
        }
    }

    private async Task _ShutdownCoreAsync(Task? pendingBootstrap, CancellationTokenSource? runtimeCts)
    {
        var shutdownStarted = _timeProvider.GetTimestamp();

        try
        {
            if (runtimeCts is not null)
            {
                try
                {
                    await runtimeCts.CancelAsync().ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    // Startup failure cleanup may have won the race and disposed the source.
                }
            }

            if (pendingBootstrap is not null)
            {
#pragma warning disable ERP022 // Shutdown observes startup completion but preserves its original caller-facing outcome.
                try
                {
                    await pendingBootstrap.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                }
                // ReSharper disable once EmptyGeneralCatchClause
                catch { }
#pragma warning restore ERP022
            }

            await _StopProcessorsAsync(shutdownStarted).ConfigureAwait(false);
        }
        finally
        {
            runtimeCts?.Dispose();
        }
    }

    private static async Task _ObserveEventualShutdownAsync(Task cleanupTask)
    {
#pragma warning disable ERP022 // Individual processor failures are logged by _StopProcessorsAsync.
        try
        {
            await cleanupTask.WaitAsync(CancellationToken.None).ConfigureAwait(false);
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

        List<Exception>? failures = null;

        foreach (var item in _processors.Reverse())
        {
            Task? stopTask = null;
            try
            {
                var remaining = options.Value.ShutdownTimeout - _timeProvider.GetElapsedTime(shutdownStarted);
                stopTask = item is IProcessingServerShutdown bounded
                    ? bounded.StopAsync(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero).AsTask()
                    : item.DisposeAsync().AsTask();

                if (remaining <= TimeSpan.Zero)
                {
                    logger.ProcessorStopFailed(
                        new TimeoutException("The shared messaging shutdown deadline has expired."),
                        item.GetType().FullName ?? item.GetType().Name
                    );
                    _ = _ObserveEventualShutdownAsync(stopTask);
                    continue;
                }

                await stopTask.WaitAsync(remaining, _timeProvider, CancellationToken.None).ConfigureAwait(false);
            }
            catch (TimeoutException ex)
            {
                logger.ProcessorStopFailed(ex, item.GetType().FullName ?? item.GetType().Name);
                if (stopTask is not null)
                {
                    _ = _ObserveEventualShutdownAsync(stopTask);
                }
            }
            catch (OperationCanceledException ex)
            {
                logger.ExpectedOperationCanceledException(ex, ex.Message);
            }
            catch (Exception ex)
            {
                // Continue shutting down remaining processors instead of aborting on the first
                // failure — partial shutdown leaves orphaned subscriptions/leases. Collect and
                // surface all failures via AggregateException so callers can diagnose.
                logger.ProcessorStopFailed(ex, item.GetType().FullName ?? item.GetType().Name);
                failures ??= [];
                failures.Add(ex);
            }
        }

        if (failures is { Count: > 0 })
        {
            throw new AggregateException("One or more messaging processors failed to stop cleanly.", failures);
        }
    }

    private void _DrainPendingMessageRegistrations()
    {
        SetupMessaging.DrainPendingMessageRegistrations(serviceProvider, options.Value);
    }
}
