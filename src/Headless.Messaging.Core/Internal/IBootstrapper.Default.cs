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
    private bool _disposed;
    private CancellationTokenSource? _cts;
    public bool IsStarted => !_cts?.IsCancellationRequested ?? false;

    public async Task BootstrapAsync(CancellationToken cancellationToken = default)
    {
        if (_cts is not null)
        {
            logger.MessagingAlreadyStarted();

            return;
        }

        logger.MessagingStarting();

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _CheckRequirement();

        try
        {
            await storageInitializer.InitializeAsync(_cts.Token).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not InvalidOperationException)
        {
            logger.StorageInitFailed(e);
        }

        _cts.Token.Register(() =>
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
        });

        await _BootstrapCoreAsync().ConfigureAwait(false);

        _disposed = false;
        logger.MessagingStarted();
    }

    private async Task _BootstrapCoreAsync()
    {
        foreach (var item in processors)
        {
            try
            {
                _cts!.Token.ThrowIfCancellationRequested();

                await item.StartAsync(_cts!.Token);
            }
            catch (OperationCanceledException)
            {
                // ignore
            }
            catch (Exception ex)
            {
                logger.ProcessorsStartedError(ex);
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await BootstrapAsync(stoppingToken).ConfigureAwait(false);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_cts != null)
        {
            await _cts.CancelAsync();
        }

        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    private void _CheckRequirement()
    {
        var marker = serviceProvider.GetService<MessagingMarkerService>();
        if (marker == null)
        {
            throw new InvalidOperationException(
                "AddMessaging() must be added on the service collection.   eg: services.AddMessaging(...)"
            );
        }

        var messageQueueMarker = serviceProvider.GetService<MessageQueueMarkerService>();
        if (messageQueueMarker == null)
        {
            logger.LogWarning(
                "No transport provider configured — messaging transport features are disabled. Scheduling-only mode is active."
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
                    + "========   eg: services.AddMessaging( options => { options.UseSqlServer(...) }); ========"
                    + Environment.NewLine
                    + "==================================================================================="
            );
        }
    }

    public override void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _disposed = true;

        base.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();

        return ValueTask.CompletedTask;
    }
}
