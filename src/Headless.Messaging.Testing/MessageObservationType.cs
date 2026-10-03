// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Internal;
using MsgHeaders = Headless.Messaging.Headers;

namespace Headless.Messaging.Testing;

/// <summary>Identifies which observable collection a recorded message belongs to.</summary>
/// <remarks>
/// Additional members may be added in future versions, so consumers that switch on this enum should
/// include a default branch to handle values they do not recognize.
/// </remarks>
[PublicAPI]
public enum MessageObservationType
{
    /// <summary>Message was published.</summary>
    Published = 0,

    /// <summary>Message was consumed successfully.</summary>
    Consumed = 1,

    /// <summary>Message processing faulted.</summary>
    Faulted = 2,

    /// <summary>
    /// The message failed for good and the framework invoked <c>RetryPolicy.OnExhausted</c>: the retry budget was
    /// spent, a fail rule or the built-in permanent set ended it, its payload failed to deserialize, its consumer is no
    /// longer registered, or it was poisoned on arrival.
    /// Recorded BEFORE the user-supplied callback runs, so a hanging or throwing callback
    /// cannot lose the observation.
    /// </summary>
    Exhausted = 3,
}
