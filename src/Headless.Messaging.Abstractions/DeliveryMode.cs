// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.UnitOfWork;

namespace Headless.Messaging;

/// <summary>
/// Controls the durability axis of an outbound message: whether it is captured durably before dispatch, or sent
/// directly to the transport. Whether the durable capture enlists in the caller's active unit of work is a
/// separate axis, controlled by <see cref="TransactionEnlistment"/> (<see cref="MessageOptions.Enlistment"/>).
/// The requested mode travels in the delivery headers by name; the framework resolves it before any effect and
/// rejects a publish it cannot honor instead of silently downgrading it.
/// </summary>
[PublicAPI]
public enum DeliveryMode
{
    /// <summary>
    /// Captures the message durably before dispatch. When a compatible unit of work is active and
    /// <see cref="TransactionEnlistment"/> allows it, the row is written on the unit's transaction and
    /// dispatched after it completes; otherwise it is stored standalone and the relay dispatches it. This is
    /// the default.
    /// </summary>
    Durable = 0,

    /// <summary>
    /// Sends directly to the transport without durable capture, regardless of the active unit of work.
    /// Cannot be combined with a delay, an absolute schedule, or <see cref="TransactionEnlistment.Required"/>.
    /// </summary>
    Direct = 1,
}
