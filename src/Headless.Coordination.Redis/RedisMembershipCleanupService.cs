// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Headless.Coordination.Redis;

internal sealed class RedisMembershipCleanupService(
    RedisMembershipStore store,
    IOptions<RedisCoordinationOptions> redisOptions,
    TimeProvider timeProvider
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(redisOptions.Value.RedisCleanupInterval, timeProvider);

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await store.CleanupAsync(stoppingToken).ConfigureAwait(false);
        }
    }
}
