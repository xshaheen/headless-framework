// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>Controls whether an outbound message is sent directly or captured durably.</summary>
[PublicAPI]
public enum DeliveryMode
{
    /// <summary>
    /// Uses durable capture when compatible commit coordination is active or a delay is requested.
    /// With no coordination it sends directly; an active incompatible boundary is rejected.
    /// </summary>
    Auto = 0,

    /// <summary>Always captures the message durably before dispatch.</summary>
    Durable = 1,

    /// <summary>
    /// Sends directly to the transport without durable capture, regardless of ambient commit coordination.
    /// </summary>
    TransportDirect = 2,
}
