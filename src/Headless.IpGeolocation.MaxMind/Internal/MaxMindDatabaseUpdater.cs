// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.IpGeolocation.Internal;

/// <summary>Keeps the databases current: checks MaxMind at startup and on an interval, and swaps in new releases.</summary>
internal sealed partial class MaxMindDatabaseUpdater(
    MaxMindDatabaseSync sync,
    IOptions<MaxMindOptions> options,
    TimeProvider timeProvider,
    ILogger<MaxMindDatabaseUpdater> logger
) : BackgroundService
{
    private readonly MaxMindOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.HasCredentials)
        {
            return;
        }

        try
        {
            // The warm-up makes the first check of every edition when one is missing, so checking here as well
            // would only repeat it; the next check is due an interval later.
            if (sync.NeedsInitialDownload)
            {
                await Task.Delay(_options.UpdateCheckInterval, timeProvider, stoppingToken).ConfigureAwait(false);
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                var succeeded = await CheckAllAsync(stoppingToken).ConfigureAwait(false);
                var delay = succeeded ? _options.UpdateCheckInterval : _options.RetryDelay;

                await Task.Delay(delay, timeProvider, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The host is stopping.
        }
    }

    /// <summary>Checks every edition once.</summary>
    /// <returns><see langword="true" /> when every check succeeded.</returns>
    internal async Task<bool> CheckAllAsync(CancellationToken cancellationToken)
    {
        var failures = await sync.UpdateAllAsync(cancellationToken).ConfigureAwait(false);

        // A failed check keeps the current database and must not end the loop and with it the host.
        foreach (var (editionId, error) in failures)
        {
            LogUpdateFailed(logger, error, editionId, _options.RetryDelay);
        }

        return failures.Count == 0;
    }

    [LoggerMessage(
        EventId = 11,
        EventName = "MaxMindDatabaseUpdateFailed",
        Level = LogLevel.Warning,
        Message = "Updating the MaxMind {EditionId} database failed; keeping the current one and retrying in {RetryDelay}"
    )]
    private static partial void LogUpdateFailed(
        ILogger logger,
        Exception exception,
        string editionId,
        TimeSpan retryDelay
    );
}
