// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Messages.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Framework.Messages.RedisStreams;

internal class RedisConsumerClientFactory(
    IOptions<CapRedisOptions> redisOptions,
    IRedisStreamManager redis,
    ILogger<RedisConsumerClient> logger
) : IConsumerClientFactory
{
    public Task<IConsumerClient> CreateAsync(string groupName, byte groupConcurrent)
    {
        var client = new RedisConsumerClient(groupName, groupConcurrent, redis, redisOptions, logger);
        return Task.FromResult<IConsumerClient>(client);
    }
}
