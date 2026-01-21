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
            logger.LogInformation("### Messaging background task is already started!");

            return;
        }

        logger.LogDebug("### Messaging background task is starting.");

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _CheckRequirement();

        try
        {
            await storageInitializer.InitializeAsync(_cts.Token).AnyContext();
        }
        catch (Exception e) when (e is not InvalidOperationException)
        {
            logger.LogError(e, "Initializing the storage structure failed!");
        }

        _cts.Token.Register(() =>
        {
            logger.LogDebug("### Messaging background task is stopping.");

            foreach (var item in processors)
            {
                try
                {
                    item.Dispose();
                }
                catch (OperationCanceledException ex)
                {
                    logger.ExpectedOperationCanceledException(ex);
                }
            }
        });

        await _BootstrapCoreAsync().AnyContext();

        _disposed = false;
        logger.LogInformation("### Messaging system started!");
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
        await BootstrapAsync(stoppingToken).AnyContext();
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_cts != null)
        {
            await _cts.CancelAsync();
        }

        await base.StopAsync(cancellationToken).AnyContext();
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
            throw new InvalidOperationException(
                "You must be config transport provider for the messaging system!"
                    + Environment.NewLine
                    + "=================================================================================="
                    + Environment.NewLine
                    + "========   eg: services.AddMessaging( options => { options.UseRabbitMq(...) }); ========"
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
