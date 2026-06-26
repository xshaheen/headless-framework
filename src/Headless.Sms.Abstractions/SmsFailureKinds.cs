// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Net.Sockets;
using Headless.Checks;

namespace Headless.Sms;

/// <summary>
/// Maps transport-level signals to a <see cref="SmsFailureKind"/> so providers classify failures consistently
/// for retry and provider-routing decisions.
/// </summary>
[PublicAPI]
public static class SmsFailureKinds
{
    /// <summary>Classifies an HTTP status code returned by an SMS provider's REST API.</summary>
    /// <param name="statusCode">The HTTP status code of the provider response.</param>
    /// <returns>
    /// <see cref="SmsFailureKind.AuthFailure"/> for 401/403, <see cref="SmsFailureKind.OutOfCredit"/> for 402,
    /// <see cref="SmsFailureKind.RateLimited"/> for 429, <see cref="SmsFailureKind.Transient"/> for 408 and any
    /// 5xx, and <see cref="SmsFailureKind.Unknown"/> otherwise. A success status returns
    /// <see cref="SmsFailureKind.None"/>.
    /// </returns>
    public static SmsFailureKind FromHttpStatusCode(HttpStatusCode statusCode)
    {
        return statusCode switch
        {
            >= HttpStatusCode.OK and < HttpStatusCode.MultipleChoices => SmsFailureKind.None,
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => SmsFailureKind.AuthFailure,
            HttpStatusCode.PaymentRequired => SmsFailureKind.OutOfCredit,
            HttpStatusCode.TooManyRequests => SmsFailureKind.RateLimited,
            HttpStatusCode.RequestTimeout or >= HttpStatusCode.InternalServerError => SmsFailureKind.Transient,
            _ => SmsFailureKind.Unknown,
        };
    }

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
