// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Coordination.Redis;

internal sealed partial class RedisMembershipCleanupService(
    RedisMembershipStore store,
    IOptions<RedisCoordinationOptions> redisOptions,
    TimeProvider timeProvider,
    ILogger<RedisMembershipCleanupService> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(redisOptions.Value.RedisCleanupInterval, timeProvider);

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await store.CleanupAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // A transient store failure must not fault the BackgroundService; retry on the next tick.
                LogCleanupFailed(logger, ex);
            }
        }
    }

    [LoggerMessage(
        EventId = 1,
        EventName = "RedisCoordinationCleanupFailed",
        Level = LogLevel.Error,
        Message = "Redis coordination cleanup tick failed; retrying on the next interval."
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void LogCleanupFailed(ILogger logger, Exception exception);
}
