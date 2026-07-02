// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Sockets;
using Headless.Checks;

namespace Headless.Sms;

/// <summary>
/// Maps transport-level signals to a <see cref="SmsFailureKind"/> so providers classify failures consistently
/// for retry and provider-routing decisions. Provider-specific signals (typed SDK exceptions, documented
/// response contracts) are classified by each provider from its own contract — never inferred from generic
/// HTTP semantics, which differ per backend.
/// </summary>
[PublicAPI]
public static class SmsFailureKinds
{
    /// <summary>Classifies an exception caught while dispatching an SMS send.</summary>
    /// <remarks>
    /// Network/transport faults (<see cref="HttpRequestException"/>, <see cref="IOException"/>,
    /// <see cref="TimeoutException"/>, <see cref="SocketException"/>) are reported as
    /// <see cref="SmsFailureKind.Transient"/> because a retry may succeed. Everything else (deserialization
    /// errors, provider-SDK contract violations, programming errors) is <see cref="SmsFailureKind.Unknown"/>,
    /// since blindly retrying it is unlikely to help.
    /// </remarks>
    /// <param name="exception">The caught exception. Must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="exception"/> is <see langword="null"/>.</exception>
    public static SmsFailureKind FromException(Exception exception)
    {
        Argument.IsNotNull(exception);

        return exception switch
        {
            HttpRequestException or IOException or TimeoutException or SocketException => SmsFailureKind.Transient,
            _ => SmsFailureKind.Unknown,
        };
    }
}
