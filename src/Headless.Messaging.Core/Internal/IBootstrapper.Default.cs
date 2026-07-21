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
    private IReadOnlyList<IProcessingServer> _processors = [];
    private bool _disposed;
    private bool _isStopping;
    private CancellationTokenSource? _runtimeCts;
    private CancellationTokenRegistration _stoppingRegistration;
    private Task? _bootstrapTask;

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

            var registration = runtimeCts.Token.Register(_StopProcessors);
            var wasStopping = false;

            lock (_bootstrapLock)
            {
                if (_isStopping)
                {
                    // Shutdown began while we were starting — undo immediately.
                    _bootstrapTask = null;
                    _isStarted = false;
                    _stoppingRegistration = default;
                    wasStopping = true;
                }
                else
                {
                    _stoppingRegistration = registration;
                    _isStarted = true;
                    _bootstrapTask = null;
                }
            }

            if (wasStopping)
            {
                await registration.DisposeAsync().ConfigureAwait(false);
                await _StopProcessorsAsync().ConfigureAwait(false);
                runtimeCts.Dispose();
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

            await _StopProcessorsAsync().ConfigureAwait(false);
            await _stoppingRegistration.DisposeAsync().ConfigureAwait(false);

            lock (_bootstrapLock)
            {
                if (ReferenceEquals(_runtimeCts, runtimeCts))
                {
                    _runtimeCts = null;
                }

                _bootstrapTask = null;
                _isStarted = false;
            }

            runtimeCts.Dispose();
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
        CancellationTokenSource? runtimeCts;
        CancellationTokenRegistration stoppingRegistration;

        lock (_bootstrapLock)
        {
            _isStopping = true;
            Volatile.Write(ref _isStarted, value: false);
            runtimeCts = _runtimeCts;
            _runtimeCts = null;
            stoppingRegistration = _stoppingRegistration;
            _stoppingRegistration = default;
        }

        if (runtimeCts is not null)
        {
            try
            {
                await runtimeCts.CancelAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                // Ignore races with concurrent startup failure cleanup.
            }
        }

        await stoppingRegistration.DisposeAsync().ConfigureAwait(false);
        runtimeCts?.Dispose();
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
            .Select(static consumer => new MessageRouteKey(
                consumer.MessageType,
                consumer.MessageName,
                MessageLaneCompatibility.ToLane(consumer.IntentType)
            ))
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

        foreach (var mapping in registry.GetMessageNameMappings())
        {
            var name = options.Value.ApplyMessageNamePrefix(mapping.Value);
            routes.Add(new MessageRouteKey(mapping.Key, name, MessageLane.Bus));
            routes.Add(new MessageRouteKey(mapping.Key, name, MessageLane.Queue));
        }

        foreach (var mapping in registry.GetLaneMessageNameMappings())
        {
            routes.Add(
                new MessageRouteKey(
                    mapping.Key.MessageType,
                    options.Value.ApplyMessageNamePrefix(mapping.Value),
                    mapping.Key.Lane
                )
            );
        }

        return routes;
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

        var mappings = registry.GetMessageNameMappings();

        foreach (var mapping in mappings)
        {
            var name = options.Value.ApplyMessageNamePrefix(mapping.Value);
            _TrackMessageName(namesByLane, MessageLane.Bus, name, mapping.Key);
            _TrackMessageName(namesByLane, MessageLane.Queue, name, mapping.Key);
        }

        foreach (var mapping in registry.GetLaneMessageNameMappings())
        {
            _TrackMessageName(
                namesByLane,
                mapping.Key.Lane,
                options.Value.ApplyMessageNamePrefix(mapping.Value),
                mapping.Key.MessageType
            );
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
        CancellationTokenSource? runtimeCts;
        CancellationTokenRegistration stoppingRegistration;

        lock (_bootstrapLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _isStopping = true;
            Volatile.Write(ref _isStarted, value: false);
            runtimeCts = _runtimeCts;
            _runtimeCts = null;
            stoppingRegistration = _stoppingRegistration;
            _stoppingRegistration = default;
            _bootstrapTask = null;
        }

#pragma warning disable VSTHRD103 // Dispose is synchronous by contract — CancelAsync is not available here.
        runtimeCts?.Cancel();
#pragma warning restore VSTHRD103
        stoppingRegistration.Dispose();
        runtimeCts?.Dispose();

        base.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        Task? pendingBootstrap;
        CancellationTokenSource? runtimeCts;
        CancellationTokenRegistration stoppingRegistration;

        lock (_bootstrapLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _isStopping = true;
            Volatile.Write(ref _isStarted, value: false);
            pendingBootstrap = _bootstrapTask;
            _bootstrapTask = null;
            runtimeCts = _runtimeCts;
            _runtimeCts = null;
            stoppingRegistration = _stoppingRegistration;
            _stoppingRegistration = default;
        }

        if (runtimeCts is not null)
        {
            await runtimeCts.CancelAsync().ConfigureAwait(false);
        }

        if (pendingBootstrap is not null)
        {
#pragma warning disable ERP022 // We just need the in-flight bootstrap to finish its cleanup; the outcome does not matter.
            try
            {
                await pendingBootstrap.ConfigureAwait(false);
            }
            // ReSharper disable once EmptyGeneralCatchClause
            catch { }
#pragma warning restore ERP022
        }

        await stoppingRegistration.DisposeAsync().ConfigureAwait(false);
        runtimeCts?.Dispose();

        base.Dispose();
    }

    private async Task _StopProcessorsAsync()
    {
        logger.MessagingStopping();

        List<Exception>? failures = null;

        foreach (var item in _processors)
        {
            try
            {
                await item.DisposeAsync().ConfigureAwait(false);
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

    private void _StopProcessors()
    {
        // Synchronous shutdown bridge for CancellationToken.Register callbacks. The async equivalent
        // (_StopProcessorsAsync) is used everywhere else; this path preserves drainer compatibility
        // by blocking the cancellation callback until the processors stop.
        logger.MessagingStopping();

        foreach (var item in _processors)
        {
            try
            {
#pragma warning disable MA0045 // CancellationToken.Register callback must block until processor disposal finishes.
                item.DisposeAsync().AsTask().GetAwaiter().GetResult();
#pragma warning restore MA0045
            }
            catch (OperationCanceledException ex)
            {
                logger.ExpectedOperationCanceledException(ex, ex.Message);
            }
        }
    }
}
