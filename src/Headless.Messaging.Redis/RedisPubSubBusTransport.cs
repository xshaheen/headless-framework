// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Headless.Messaging.Redis;

internal sealed class RedisPubSubBusTransport(
    IRedisPubSubConnectionProvider connectionProvider,
    IOptions<RedisPubSubOptions> options,
    ILogger<RedisPubSubBusTransport> logger
) : IBusTransport
{
    public BrokerAddress BrokerAddress => new("redis_pubsub", options.Value.DisplayEndpoint);

    public async Task<OperateResult> SendAsync(TransportMessage message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var connection = await connectionProvider.ConnectAsync().ConfigureAwait(false);
            var subscriber = connection.GetSubscriber();
            var messageName = message.Name;
            var receivers = await subscriber
                .PublishAsync(RedisChannel.Literal(messageName), RedisPubSubEnvelope.Serialize(message))
                .ConfigureAwait(false);

            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.MessagePublished(messageName, receivers);
            }

            return OperateResult.Success;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return OperateResult.Failed(new PublisherSentFailedException(ex.Message, ex));
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal static partial class RedisPubSubBusTransportLog
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Debug,
        Message = "Redis Pub/Sub message [{Message}] published to {Receivers} subscriber(s)."
    )]
    public static partial void MessagePublished(this ILogger logger, string message, long receivers);
}
