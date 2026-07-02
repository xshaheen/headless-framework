// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Tus.Services;
using tusdotnet.Interfaces;

namespace Demo.Services;

/// <summary>
/// Periodically removes expired <em>incomplete</em> uploads via
/// <see cref="ITusExpirationStore.RemoveExpiredFilesAsync"/>. Completed uploads are never touched —
/// the store only reaps unfinished uploads whose (sliding) expiration has passed.
/// </summary>
public sealed partial class ExpiredUploadsCleanupService(
    TusAzureStore store,
    ILogger<ExpiredUploadsCleanupService> logger
) : BackgroundService
{
    private static readonly TimeSpan _Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_Interval);

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                var removed = await store.RemoveExpiredFilesAsync(stoppingToken).ConfigureAwait(false);

                if (removed > 0)
                {
                    LogRemovedExpiredUploads(logger, removed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                LogCleanupFailed(logger, e);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Removed {Count} expired incomplete upload(s)")]
    private static partial void LogRemovedExpiredUploads(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Expired-upload cleanup pass failed")]
    private static partial void LogCleanupFailed(ILogger logger, Exception exception);
}
