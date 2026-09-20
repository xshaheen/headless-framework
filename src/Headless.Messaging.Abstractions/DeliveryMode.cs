// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Controls the durability axis of an autonomous outbound message: whether it is captured durably before
/// dispatch, or sent directly to the transport. It is carried by <c>PublishOptions</c> and
/// <c>QueueOptions</c> only; the enlisted outbox surface has no mode, because durable capture is how
/// that surface joins the caller's transaction. The requested mode travels in the delivery headers by name;
/// the framework resolves it before any effect and rejects a publish it cannot honor instead of silently
/// downgrading it.
/// </summary>
[PublicAPI]
public enum DeliveryMode
{
    /// <summary>
    /// Captures the message durably before dispatch, then the relay dispatches it. This is the default.
    /// </summary>
    Durable = 0,

    /// <summary>
    /// Sends directly to the transport without durable capture. Cannot be combined with a delay or an
    /// absolute schedule.
    /// </summary>
    Direct = 1,
}
