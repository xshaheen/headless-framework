// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Redis;

internal sealed class RedisBusTransport(
    IRedisStreamManager redis,
    IOptions<RedisMessagingOptions> options,
    ILogger<RedisBusTransport> logger
) : IBusTransport
{
    private readonly RedisMessagingOptions _options = options.Value;

    public BrokerAddress BrokerAddress => new("redis", _options.DisplayEndpoint);

    public async Task<OperateResult> SendAsync(TransportMessage message, CancellationToken cancellationToken = default)
    {
        Configuration.MessagingRoutingAffinityMapping.RejectUnsupported(message, "Redis");
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await redis
                .PublishAsync(
                    RedisPhysicalAddress.BusStream(message.Name),
                    message.AsStreamEntries(),
                    cancellationToken
                )
                .ConfigureAwait(false);

            logger.BusMessagePublished(message.Name);
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

internal static partial class RedisBusTransportLog
{
    [LoggerMessage(EventId = 3010, Level = LogLevel.Debug, Message = "Redis Bus message [{Message}] was published.")]
    public static partial void BusMessagePublished(this ILogger logger, string message);
}
