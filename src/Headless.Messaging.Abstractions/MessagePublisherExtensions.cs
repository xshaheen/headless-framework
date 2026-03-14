// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Convenience overloads for the unified publisher contracts.
/// </summary>
public static class MessagePublisherExtensions
{
    /// <summary>
    /// Publishes a message with the default publish options.
    /// </summary>
    public static Task PublishAsync<T>(
        this IMessagePublisher publisher,
        T? contentObj,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(publisher);
        return publisher.PublishAsync(contentObj, options: null, cancellationToken);
    }

    /// <summary>
    /// Schedules a delayed publish with the default publish options.
    /// </summary>
    public static Task PublishDelayAsync<T>(
        this IOutboxPublisher publisher,
        TimeSpan delayTime,
        T? contentObj,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(publisher);
        return publisher.PublishDelayAsync(delayTime, contentObj, options: null, cancellationToken);
    }
}
