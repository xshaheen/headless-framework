// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Internal;

/// <summary>
/// Ends a message whose delivery deadline (<c>MessageOptions.DeliverWithin</c>) passed before it could be delivered.
/// It is recorded on the failed row so the dashboard shows why the transport, or the consumer, never ran.
/// </summary>
#pragma warning disable CA1064 // Deliberately internal: both drop sites map it to their terminal write; no caller catches it.
internal sealed class MessageDeliverByExceededException(DateTimeOffset? deadline)
    : Exception(
        "deliver_by_exceeded: the message's delivery deadline "
            + $"{deadline?.ToString("O", CultureInfo.InvariantCulture)} passed, so it was dropped instead of delivered."
    );
#pragma warning restore CA1064
