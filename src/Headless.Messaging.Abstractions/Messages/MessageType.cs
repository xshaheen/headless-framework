// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Messages;

/// <summary>
/// Specifies the type of message in the messaging system.
/// </summary>
public enum MessageType
{
    /// <summary>
    /// Indicates a message that was published by the application and is waiting for delivery to subscribers.
    /// These are outgoing messages.
    /// </summary>
    Publish,

    /// <summary>
    /// Indicates a message that was received by the subscriber from the message broker.
    /// These are incoming messages.
    /// </summary>
    Subscribe,
}
