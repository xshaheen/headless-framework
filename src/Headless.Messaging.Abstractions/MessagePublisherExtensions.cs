// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

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
        Argument.IsNotNull(publisher);
        return publisher.PublishAsync(contentObj, options: null, cancellationToken);
    }

    /// <summary>
    /// Schedules a delayed publish with the default publish options.
    /// </summary>
    public static Task PublishDelayAsync<T>(
        this IScheduledPublisher publisher,
        TimeSpan delayTime,
        T? contentObj,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(publisher);
        return publisher.PublishDelayAsync(delayTime, contentObj, options: null, cancellationToken);
    }
}
