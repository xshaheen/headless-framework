// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Microsoft.Extensions.Logging;

namespace Headless.Caching;

/// <summary>
/// Message consumer that handles cache invalidation messages from other instances.
/// Registered automatically by <see cref="HybridCacheSetup"/>.
/// </summary>
[PublicAPI]
public sealed class HybridCacheInvalidationConsumer(HybridCache cache, ILogger<HybridCacheInvalidationConsumer> logger)
    : IConsume<CacheInvalidationMessage>
{
    /// <inheritdoc />
    public async ValueTask Consume(
        ConsumeContext<CacheInvalidationMessage> context,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await cache.HandleInvalidationAsync(context.Message, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected during shutdown, don't log as error
            throw;
        }
        catch (Exception ex)
        {
            var msg = context.Message;
            logger.LogFailedToProcessCacheInvalidation(
                ex,
                msg.InstanceId,
                msg.Keys?.Length ?? (msg.Key is not null ? 1 : 0),
                msg.Prefix is not null,
                msg.FlushAll
            );
        }
    }
}

internal static partial class HybridCacheInvalidationConsumerLoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        EventName = "FailedToProcessCacheInvalidation",
        Level = LogLevel.Error,
        Message = "Failed to process cache invalidation message (instanceId={InstanceId}, keyCount={KeyCount}, hasPrefix={HasPrefix}, flushAll={FlushAll})"
    )]
    public static partial void LogFailedToProcessCacheInvalidation(
        this ILogger logger,
        Exception exception,
        string? instanceId,
        int keyCount,
        bool hasPrefix,
        bool flushAll
    );
}
