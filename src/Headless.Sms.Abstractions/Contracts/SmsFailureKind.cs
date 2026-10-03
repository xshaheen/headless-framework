// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Sms;

/// <summary>Classifies why an SMS send failed, to inform retry and provider-routing decisions.</summary>
/// <remarks>
/// New members may be added in minor versions as providers surface finer-grained failure signals. Consumers that
/// <see langword="switch"/> on this enum must always handle <see cref="Unknown"/> / the <see langword="default"/> case so a newly added
/// member degrades to "treat as unknown" rather than falling through unhandled.
/// </remarks>
[PublicAPI]
public enum SmsFailureKind
{
    /// <summary>The send succeeded; no failure. This is the default for a successful response.</summary>
    None = 0,

    /// <summary>The failure cause is unknown or could not be classified.</summary>
    Unknown = 1,

    /// <summary>A transient transport/network fault (timeout, connection reset). May succeed on retry.</summary>
    Transient = 2,

    /// <summary>The provider rejected the request because of rate limiting.</summary>
    RateLimited = 3,

    /// <summary>The recipient address was invalid, unreachable, or rejected by the provider.</summary>
    InvalidRecipient = 4,

    /// <summary>Authentication or authorization with the provider failed.</summary>
    AuthFailure = 5,

    /// <summary>The provider account has insufficient credit or balance.</summary>
    OutOfCredit = 6,
}
