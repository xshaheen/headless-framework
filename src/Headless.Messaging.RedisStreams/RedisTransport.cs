// Copyright (c) Mahmoud Shaheen. All rights reserved.

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

    public BrokerAddress BrokerAddress => new("redis", _options.DisplayEndpoint);

    public async Task<OperateResult> SendAsync(TransportMessage message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await redis.PublishAsync(message.GetName(), message.AsStreamEntries()).ConfigureAwait(false);

            var messageName = message.GetName();
            logger.MessagePublished(messageName);

            return OperateResult.Success;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var wrapperEx = new PublisherSentFailedException(ex.Message, ex);

            return OperateResult.Failed(wrapperEx);
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal static partial class RedisTransportLog
{
    [LoggerMessage(EventId = 3003, Level = LogLevel.Debug, Message = "Redis message [{Message}] has been published.")]
    public static partial void MessagePublished(this ILogger logger, string message);
}
