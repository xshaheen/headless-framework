// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Messaging.Configuration;
using Headless.Messaging.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Internal;

/// <summary>Default implement of <see cref="IBootstrapper" />.</summary>
internal sealed class Bootstrapper(
    IEnumerable<IProcessingServer> processors,
    IStorageInitializer storageInitializer,
    IServiceProvider serviceProvider,
    IOptions<MessagingOptions> options,
    ILogger<IBootstrapper> logger
) : BackgroundService, IBootstrapper
{
    private readonly Lock _bootstrapLock = new();
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

            try
            {
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

        foreach (var item in processors)
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
            Volatile.Write(ref _isStarted, false);
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

        var lockProvider = serviceProvider.GetRequiredKeyedService<IDistributedLockProvider>(
            MessagingKeys.LockProvider
        );

        // Direct type check on the public sentinel — sealed, so the test is exact. A user
        // who deliberately wraps a NullDistributedLockProvider in a decorator will bypass
        // this warning; that's an opt-out we accept rather than guard against, since the
        // sentinel exists specifically to be detectable.
        if (lockProvider is not NullDistributedLockProvider)
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
        IDistributedLockProvider? unkeyedProvider = null;
        try
        {
            unkeyedProvider = serviceProvider.GetService<IDistributedLockProvider>();
        }
#pragma warning disable RCS1075, ERP022 // Intentional: probe failure must not block startup. EventId 77 fallback emits below.
        catch (Exception)
        {
            // Intentional: probe failure must not block startup. EventId 77 fallback emits below.
        }
#pragma warning restore RCS1075, ERP022

        if (unkeyedProvider is not null and not NullDistributedLockProvider)
        {
            logger.UseStorageLockWithNoOpProviderButRealUnkeyed();
            return;
        }

        logger.UseStorageLockWithNoOpProvider();
    }

    private void _CheckRequirement()
    {
        var marker = serviceProvider.GetService<MessagingMarkerService>();
        if (marker == null)
        {
            throw new InvalidOperationException(
                "AddHeadlessMessaging() must be added on the service collection.   eg: services.AddHeadlessMessaging(...)"
            );
        }

        var messageQueueMarker = serviceProvider.GetService<MessageQueueMarkerService>();
        if (messageQueueMarker == null)
        {
            throw new InvalidOperationException(
                "Messaging requires a transport provider. Register a native IBusTransport/IQueueTransport "
                    + "(e.g., UseRabbitMQ, UseKafka, UseAzureServiceBus) or register an ITransport so the "
                    + "legacy adapter applies."
                    + Environment.NewLine
                    + "Example: services.AddHeadlessMessaging(setup => { setup.UseRabbitMq(...); });"
            );
        }

        var databaseMarker = serviceProvider.GetService<MessageStorageMarkerService>();

        if (databaseMarker == null)
        {
            throw new InvalidOperationException(
                "Messaging requires a storage provider. Register one (e.g., UseSqlServer, UsePostgreSql, "
                    + "UseInMemoryStorage) so persisted publishes and the inbox/outbox can be backed."
                    + Environment.NewLine
                    + "Example: services.AddHeadlessMessaging(setup => { setup.UseSqlServer(...); });"
            );
        }

        _CheckIntentTransportSupport();
    }

    private void _CheckIntentTransportSupport()
    {
        var registry = serviceProvider.GetService<IConsumerRegistry>();
        var consumers = registry?.GetAll() ?? [];

        var consumerRequiresBus = consumers.Any(static consumer => consumer.IntentType == IntentType.Bus);
        var consumerRequiresQueue = consumers.Any(static consumer => consumer.IntentType == IntentType.Queue);

        // Publisher-side fences: if any IBus/IOutboxBus (or queue equivalents) is resolvable,
        // a matching IBusTransport/IQueueTransport must be wired before bootstrap completes.
        var publisherRequiresBus =
            serviceProvider.GetService<IBus>() is not null || serviceProvider.GetService<IOutboxBus>() is not null;
        var publisherRequiresQueue =
            serviceProvider.GetService<IQueue>() is not null || serviceProvider.GetService<IOutboxQueue>() is not null;

        _RequireTransportFor<IBusTransport>(
            "bus",
            consumerRequiresBus || publisherRequiresBus,
            consumerSide: consumerRequiresBus,
            publisherSide: publisherRequiresBus
        );

        _RequireTransportFor<IQueueTransport>(
            "queue",
            consumerRequiresQueue || publisherRequiresQueue,
            consumerSide: consumerRequiresQueue,
            publisherSide: publisherRequiresQueue
        );
    }

    private void _RequireTransportFor<TTransport>(string intent, bool required, bool consumerSide, bool publisherSide)
        where TTransport : class
    {
        if (!required)
        {
            return;
        }

        if (serviceProvider.GetService<TTransport>() is not null)
        {
            return;
        }

        var caller = (consumerSide, publisherSide) switch
        {
            (true, true) => $"Add{intent}Consumer<...>/I{intent} publisher",
            (true, false) => $"Add{intent}Consumer<...>",
            (false, true) => $"I{intent}/IOutbox{intent} publisher",
            _ => $"{intent} subsystem",
        };

        throw new InvalidOperationException(
            $"{caller} was registered but no I{(intent == "bus" ? "Bus" : "Queue")}Transport (or "
                + "ITransport bridged via the legacy adapter) is available. Register a "
                + $"{intent}-capable transport provider before messaging bootstrap starts."
        );
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
            Volatile.Write(ref _isStarted, false);
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
            Volatile.Write(ref _isStarted, false);
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

        foreach (var item in processors)
        {
            try
            {
                await item.DisposeAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException ex)
            {
                logger.ExpectedOperationCanceledException(ex, ex.Message);
            }
        }
    }

    private void _StopProcessors()
    {
        // Synchronous shutdown bridge for CancellationToken.Register callbacks. The async equivalent
        // (_StopProcessorsAsync) is used everywhere else; this path preserves drainer compatibility
        // by blocking the cancellation callback until the processors stop.
        logger.MessagingStopping();

        foreach (var item in processors)
        {
            try
            {
                item.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch (OperationCanceledException ex)
            {
                logger.ExpectedOperationCanceledException(ex, ex.Message);
            }
        }
    }
}
