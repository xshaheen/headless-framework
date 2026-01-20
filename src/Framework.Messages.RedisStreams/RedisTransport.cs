// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Messages.Internal;
using Framework.Messages.Messages;
using Framework.Messages.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Framework.Messages;

internal class RedisTransport(
    IRedisStreamManager redis,
    IOptions<MessagingRedisOptions> options,
    ILogger<RedisTransport> logger
) : ITransport
{
    private readonly MessagingRedisOptions _options = options.Value;

    public BrokerAddress BrokerAddress => new("redis", _options.Endpoint);

    public async Task<OperateResult> SendAsync(TransportMessage message)
    {
        try
        {
            await redis.PublishAsync(message.GetName(), message.AsStreamEntries()).AnyContext();

            logger.LogDebug("Redis message [{message}] has been published.", message.GetName());

            return OperateResult.Success;
        }
        catch (Exception ex)
        {
            var wrapperEx = new PublisherSentFailedException(ex.Message, ex);

            return OperateResult.Failed(wrapperEx);
        }
    }
}
