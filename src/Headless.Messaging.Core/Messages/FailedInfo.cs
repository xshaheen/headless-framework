// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Configuration;

namespace Headless.Messaging.Messages;

/// <summary>
/// Contains information about a message that has failed processing and exceeded the retry threshold.
/// This class is used when invoking the <see cref="RetryPolicyOptions.OnExhausted"/> callback.
/// </summary>
[PublicAPI]
public sealed class FailedInfo
{
    /// <summary>
    /// Gets the service provider for the current scope.
    /// This allows the callback to resolve dependencies from the dependency injection container.
    /// </summary>
    public required IServiceProvider ServiceProvider { get; init; }

    /// <summary>
    /// Gets the message type indicating whether this was a published or subscribed message.
    /// <see cref="MessageType.Publish"/> for messages that failed to be sent to the broker.
    /// <see cref="MessageType.Subscribe"/> for messages that failed to be processed by subscribers.
    /// </summary>
    public required MessageType MessageType { get; init; }

    /// <summary>
    /// Gets the message object that failed processing.
    /// Contains the headers and value data that caused the failure.
    /// </summary>
    public required Message Message { get; init; }

    /// <summary>
    /// Gets the exception that triggered the exhausted retry decision.
    /// </summary>
    public required Exception Exception { get; init; }
}
