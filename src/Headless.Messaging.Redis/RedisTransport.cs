// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Redis;

internal sealed class RedisTransport(
    IRedisStreamManager redis,
    IOptions<RedisMessagingOptions> options,
    ILogger<RedisTransport> logger
) : IQueueTransport
{
    private readonly RedisMessagingOptions _options = options.Value;

    // Set once DisposeAsync runs: a later send fails instead of reaching the broker.
    private int _disposed;

    public BrokerAddress BrokerAddress => new("redis", _options.DisplayEndpoint);

    public async Task<OperateResult> SendAsync(TransportMessage message, CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return OperateResult.Failed(new ObjectDisposedException(nameof(RedisTransport)));
        }

        MessagingRoutingAffinityMapping.RejectUnsupported(message, "Redis");
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await redis
                .PublishAsync(
                    RedisPhysicalAddress.QueueStream(message.Name),
                    message.AsStreamEntries(),
                    cancellationToken
                )
                .ConfigureAwait(false);

            var messageName = message.Name;
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

    public ValueTask DisposeAsync()
    {
        Volatile.Write(ref _disposed, 1);
        return ValueTask.CompletedTask;
    }
}

internal static partial class RedisTransportLog
{
    [LoggerMessage(EventId = 3003, Level = LogLevel.Debug, Message = "Redis message [{Message}] has been published.")]
    public static partial void MessagePublished(this ILogger logger, string message);
}
