// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Transport;

/// <summary>
/// Classifies a consumer-client connect failure the way the messaging core consumes it: cancellation of the
/// factory's token is always cancellation, and anything else is a broker connection failure.
/// </summary>
internal static class BrokerConnectGuard
{
    /// <summary>
    /// Returns the exception a consumer client factory must throw for a failed connect attempt: an
    /// <see cref="OperationCanceledException"/> carrying <paramref name="cancellationToken"/> when that token is
    /// cancelled, otherwise <paramref name="failure"/> wrapped in a <see cref="BrokerConnectionException"/> —
    /// or passed through when it already is one, so a terminal broker fault keeps its original inner detail.
    /// </summary>
    /// <remarks>
    /// A cancelled connect can still fail with the broker client's own exception before it observes the token —
    /// awaiting a task that races a cancelled token propagates whichever completed first — and the messaging core
    /// treats a <see cref="BrokerConnectionException"/> as a broker outage, so a wrapped shutdown would report
    /// one. Callers must still observe the token before they create the half-connected client, so an
    /// already-cancelled caller never builds one.
    /// </remarks>
    internal static Exception ConnectFailure(Exception failure, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return new OperationCanceledException(cancellationToken);
        }

        return failure is BrokerConnectionException ? failure : new BrokerConnectionException(failure);
    }
}
