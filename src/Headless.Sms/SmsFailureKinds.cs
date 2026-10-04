// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Sockets;
using Headless.Checks;
using Polly.CircuitBreaker;
using Polly.RateLimiting;
using Polly.Timeout;

namespace Headless.Sms;

/// <summary>
/// Maps transport exceptions and resilience rejections to an <see cref="SmsFailureKind"/>.
/// </summary>
[PublicAPI]
public static class SmsFailureKinds
{
    /// <summary>Classifies an exception caught during an SMS send operation.</summary>
    /// <remarks>
    /// Maps I/O faults, socket exceptions, HTTP request errors, timeouts, and Polly pipeline rejections to
    /// <see cref="SmsFailureKind.Transient"/>. Maps all other exceptions to <see cref="SmsFailureKind.Unknown"/>.
    /// </remarks>
    /// <param name="exception">The caught exception.</param>
    /// <returns>The classified <see cref="SmsFailureKind"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="exception"/> is <see langword="null"/>.</exception>
    public static SmsFailureKind FromException(Exception exception)
    {
        Argument.IsNotNull(exception);

        return exception switch
        {
            HttpRequestException or IOException or TimeoutException or SocketException => SmsFailureKind.Transient,
            // Classify Polly timeout, open-circuit, and rate-limiter rejections as transient faults.
            TimeoutRejectedException or BrokenCircuitException or RateLimiterRejectedException =>
                SmsFailureKind.Transient,
            _ => SmsFailureKind.Unknown,
        };
    }
}
