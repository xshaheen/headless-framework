// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>Why a stored message became terminal instead of being delivered again.</summary>
/// <remarks>Consumers that switch on this enum should handle unrecognized values, because more reasons may be added.</remarks>
[PublicAPI]
public enum MessageTerminalReason
{
    /// <summary>The configured retry budget permits no further attempt.</summary>
    RetriesExhausted = 0,

    /// <summary>A non-retryable failure, rejection, or invalid message ended delivery before the budget was spent.</summary>
    PermanentFailure = 1,

    /// <summary>The message's delivery deadline or a stored request's response deadline passed.</summary>
    DeliveryDeadlineExpired = 2,

    /// <summary>No registered transport can send the stored message's lane.</summary>
    NoTransport = 3,
}
