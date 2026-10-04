// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Internal;

/// <summary>
/// Reads the delivery mode off an option record whose static type is the shared <see cref="MessageOptions"/> base.
/// The mode lives on the autonomous records only, so middleware handing back a base-typed instance — and the
/// outbox records, which carry no mode at all — answer <see langword="null"/> and inherit the resolved default.
/// </summary>
internal static class MessageOptionsDelivery
{
    internal static DeliveryMode? GetDeliveryMode(MessageOptions? options) =>
        options switch
        {
            PublishOptions publishOptions => publishOptions.DeliveryMode,
            QueueOptions queueOptions => queueOptions.DeliveryMode,
            _ => null,
        };
}
