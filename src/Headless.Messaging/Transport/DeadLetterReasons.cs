// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Transport;

/// <summary>The reason codes the core passes to <see cref="IConsumerClient.DeadLetterAsync"/>.</summary>
internal static class DeadLetterReasons
{
    /// <summary>No consumer on the subscription handles the message's name.</summary>
    public const string SubscriberNotFound = "SubscriberNotFound";

    /// <summary>A receive middleware rejected the delivery as a policy decision.</summary>
    public const string ReceiveRejected = "ReceiveRejected";

    /// <summary>The payload could not be read into the consumer's message type, or was empty.</summary>
    public const string DeserializationFailed = "DeserializationFailed";

    /// <summary>The receive stage faulted for another reason, such as a contract-version mismatch or a receive middleware throwing.</summary>
    public const string ReceiveFailed = "ReceiveFailed";

    // Brokers keep the description as a message property, which counts toward the message's size limit; the full
    // exception stays in the core's poison record.
    public const int MaxDescriptionLength = 1024;
}
