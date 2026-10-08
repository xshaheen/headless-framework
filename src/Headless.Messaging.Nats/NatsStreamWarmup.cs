// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;
using NATS.Client.JetStream;

namespace Headless.Messaging.Nats;

/// <summary>
/// Creates the streams the application owns through <see cref="NatsMessagingOptions.Streams"/> when the messaging host
/// starts, so they exist before the first publish or consumer needs them.
/// </summary>
/// <remarks>
/// It runs in the background and never fails host start: a broker that is not reachable yet is logged, and the first
/// publish or consumer start ensures the stream again.
/// </remarks>
internal sealed class NatsStreamWarmup(
    NatsStreamProvisioner provisioner,
    INatsConnectionPool connectionPool,
    ILogger<NatsStreamWarmup> logger
) : IProcessingServer
{
    private readonly CancellationTokenSource _stopping = new();
    private Task _warmup = Task.CompletedTask;
    private int _disposed;

    public ValueTask StartAsync(CancellationToken stoppingToken)
    {
        if (provisioner.IsEnabled && provisioner.HasOwnedDeclaredStreams)
        {
            _warmup = _WarmUpAsync(_stopping.Token);
        }

        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _stopping.CancelAsync().ConfigureAwait(false);

        try
        {
            await _warmup.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Host shutdown cancelled the warm-up.
        }

        _stopping.Dispose();
    }

    private async Task _WarmUpAsync(CancellationToken cancellationToken)
    {
        // Leave the bootstrap's synchronous path before the first broker call.
        await Task.Yield();

        try
        {
            var js = new NatsJSContext(connectionPool.GetConnection());
            await provisioner.WarmUpAsync(js, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // Background boundary: a failure is logged, and the first publish or consumer retries.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            logger.LogNatsStreamWarmupFailed(ex);
        }
    }
}

internal static partial class NatsStreamWarmupLog
{
    [LoggerMessage(
        EventId = 16,
        EventName = "NatsStreamWarmupFailed",
        Level = LogLevel.Warning,
        Message = "Creating the declared NATS streams at startup failed; the first publish or consumer start tries again."
    )]
    public static partial void LogNatsStreamWarmupFailed(this ILogger logger, Exception exception);
}
