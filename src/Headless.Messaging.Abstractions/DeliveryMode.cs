// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Controls how an outbound message reaches the transport. The requested mode travels in the delivery headers
/// by name; the framework resolves it to <see cref="Durable"/> or <see cref="Direct"/> before any effect and
/// rejects a publish it cannot honor instead of silently downgrading it.
/// </summary>
[PublicAPI]
public enum DeliveryMode
{
    /// <summary>
    /// Captures the message durably before dispatch. Inside a compatible live commit-coordination scope the row
    /// is written on the caller's transaction and dispatched after commit; with no scope it is stored first and
    /// the relay dispatches it. An active incompatible scope is rejected before any effect. This is the default.
    /// </summary>
    Durable = 0,

    /// <summary>
    /// Requires atomicity with the caller's transaction: captures on a compatible live commit-coordination scope
    /// and dispatches after commit. Rejected before any effect when no scope is active, when no coordinator is
    /// registered, or when the active scope is incompatible with messaging storage.
    /// </summary>
    Coordinated = 1,

    /// <summary>
    /// Sends directly to the transport without durable capture, regardless of ambient commit coordination.
    /// Cannot be combined with a delay or an absolute schedule.
    /// </summary>
    Direct = 2,
}
