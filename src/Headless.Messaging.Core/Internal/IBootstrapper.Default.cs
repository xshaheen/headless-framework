// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Configuration;
using Headless.Messaging.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Headless.Messaging.Internal;

/// <summary>Default implement of <see cref="IBootstrapper" />.</summary>
internal sealed class Bootstrapper(
    IEnumerable<IProcessingServer> processors,
    IStorageInitializer storageInitializer,
    IServiceProvider serviceProvider,
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

            try
            {
                await storageInitializer.InitializeAsync(startupToken).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not InvalidOperationException)
            {
                logger.StorageInitFailed(e);
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
                _StopProcessors();
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

            _StopProcessors();
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
                "You must be config transport provider for the messaging system!"
                    + Environment.NewLine
                    + "=================================================================================="
                    + Environment.NewLine
                    + "========   eg: services.AddHeadlessMessaging( setup => { setup.UseRabbitMq(...) }); ========"
                    + Environment.NewLine
                    + "=================================================================================="
            );
        }

        var databaseMarker = serviceProvider.GetService<MessageStorageMarkerService>();

        if (databaseMarker == null)
        {
            throw new InvalidOperationException(
                "You must be config storage provider for the messaging system!"
                    + Environment.NewLine
                    + "==================================================================================="
                    + Environment.NewLine
                    + "========   eg: services.AddHeadlessMessaging( setup => { setup.UseSqlServer(...) }); ========"
                    + Environment.NewLine
                    + "==================================================================================="
            );
        }
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

    private void _StopProcessors()
    {
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
