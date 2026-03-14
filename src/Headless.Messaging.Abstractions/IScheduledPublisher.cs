// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Schedules message publishing for future delivery.
/// </summary>
public interface IScheduledPublisher
{
    /// <summary>
    /// Schedules a message for delayed delivery.
    /// </summary>
    /// <typeparam name="T">The message type.</typeparam>
    /// <param name="delayTime">How long to wait before dispatching the message.</param>
    /// <param name="contentObj">The message payload. Can be <see langword="null"/>.</param>
    /// <param name="options">Optional publish overrides for topic, correlation, and custom headers.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the delayed publish operation.</returns>
    Task PublishDelayAsync<T>(
        TimeSpan delayTime,
        T? contentObj,
        PublishOptions? options = null,
        CancellationToken cancellationToken = default
    );
}
