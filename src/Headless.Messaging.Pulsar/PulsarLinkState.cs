// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Pulsar;

/// <summary>Whether a receiver's link to the broker can deliver messages.</summary>
internal enum PulsarLinkState
{
    /// <summary>Not connected yet, or reconnecting after a lost connection; the client retries on its own.</summary>
    Disconnected,

    /// <summary>Subscribed and receiving.</summary>
    Connected,

    /// <summary>Faulted, closed, or otherwise ended; the receiver never delivers again.</summary>
    Ended,
}
