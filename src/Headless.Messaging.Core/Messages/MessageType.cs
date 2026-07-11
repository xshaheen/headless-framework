// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Messages;

/// <summary>
/// Specifies the type of message in the messaging system.
/// </summary>
/// <remarks>
/// The underlying values are a runtime discriminator (used to select the outbox vs inbox lane); they are
/// not persisted as integers. Additional members may be added in future versions, so consumers that switch
/// on this enum should include a default branch to handle values they do not recognize.
/// </remarks>
[PublicAPI]
public enum MessageType
{
    /// <summary>
    /// Indicates a message that was published by the application and is waiting for delivery to subscribers.
    /// These are outgoing messages.
    /// </summary>
    Publish = 0,

    /// <summary>
    /// Indicates a message that was received by the subscriber from the message broker.
    /// These are incoming messages.
    /// </summary>
    Subscribe = 1,
}
