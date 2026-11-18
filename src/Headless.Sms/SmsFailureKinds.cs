// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Sockets;
using Headless.Checks;
using Polly.CircuitBreaker;
using Polly.RateLimiting;

namespace Headless.Sms;

/// <summary>
/// The single classifier that maps transport-level signals to a <see cref="SmsFailureKind"/>, so providers
/// classify failures consistently for retry and provider-routing decisions. It is Polly-aware (this package
/// references Polly; <c>Headless.Sms.Abstractions</c> does not), so it lives here rather than on the
/// contract type — providers pass its result to <c>SendSingleSmsResponse.FromException(exception, kind)</c>.
/// Provider-specific signals (typed SDK exceptions, documented response contracts) are classified by each
/// provider from its own contract, never inferred from generic HTTP semantics, which differ per backend.
/// </summary>
[PublicAPI]
public static class SmsFailureKinds
{
    /// <summary>Classifies an exception caught while dispatching an SMS send.</summary>
    /// <remarks>
    /// <see cref="SmsFailureKind.Transient"/> is reserved for failures that happened before the request left this
    /// process, so a retry cannot send the message twice: a connection that could not be opened (an
    /// <see cref="HttpRequestException"/> whose <see cref="HttpRequestException.HttpRequestError"/> is a connection,
    /// name-resolution, or TLS-handshake error, or a <see cref="SocketException"/> for a refused or unreachable host),
    /// and the resilience pipeline's open-circuit and rate-limiter rejections, which refuse before sending. A timeout,
    /// a reset, or any other I/O fault can happen after the provider accepted the message, so it is
    /// <see cref="SmsFailureKind.Unknown"/>: the message may have been delivered, and a blind retry can deliver it twice.
    /// </remarks>
    /// <param name="exception">The caught exception.</param>
    /// <returns>The classified <see cref="SmsFailureKind"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="exception"/> is <see langword="null"/>.</exception>
    public static SmsFailureKind FromException(Exception exception)
    {
        Argument.IsNotNull(exception);

        return exception switch
        {
            BrokenCircuitException or RateLimiterRejectedException => SmsFailureKind.Transient,
            HttpRequestException
            {
                HttpRequestError: HttpRequestError.ConnectionError
                    or HttpRequestError.NameResolutionError
                    or HttpRequestError.SecureConnectionError,
            } => SmsFailureKind.Transient,
            SocketException
            {
                SocketErrorCode: SocketError.ConnectionRefused
                    or SocketError.HostNotFound
                    or SocketError.HostUnreachable
                    or SocketError.NetworkUnreachable,
            } => SmsFailureKind.Transient,
            _ => SmsFailureKind.Unknown,
        };
    }
}
