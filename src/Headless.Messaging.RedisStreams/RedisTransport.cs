// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.RedisStreams;

internal sealed class RedisTransport(
    IRedisStreamManager redis,
    IOptions<MessagingRedisOptions> options,
    ILogger<RedisTransport> logger
) : ITransport
{
    private readonly MessagingRedisOptions _options = options.Value;

    public BrokerAddress BrokerAddress => new("redis", _options.Endpoint);

    public async Task<OperateResult> SendAsync(TransportMessage message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await redis.PublishAsync(message.GetName(), message.AsStreamEntries()).ConfigureAwait(false);

            logger.LogDebug("Redis message [{Message}] has been published.", message.GetName());

            return OperateResult.Success;
        }
        catch (Exception ex)
        {
            var wrapperEx = new PublisherSentFailedException(ex.Message, ex);

            return OperateResult.Failed(wrapperEx);
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
