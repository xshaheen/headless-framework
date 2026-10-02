// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Thrown when a request never left the caller: publish middleware suppressed it, the transport reported the send as
/// failed, the requester was stopping, or its reply listener did not become ready within the timeout.
/// </summary>
/// <remarks>
/// When middleware suppressed the request, the requester was stopping, or the listener never became ready, the request
/// never reached the broker, so retrying the call cannot repeat work. When the transport reported the send as failed,
/// <see cref="Exception.InnerException"/> is the transport's exception; the broker normally rejected it, but a
/// connection lost after the broker stored the request can also surface this way, so a responder that must not run
/// twice should still be idempotent. A send that outlasts the transport publish timeout is not reported here: the
/// broker may have taken it, so the call keeps waiting for the reply.
/// </remarks>
/// <param name="message">Why the request was not sent.</param>
/// <param name="requestId">The request's identifier, or <see langword="null"/> when none was assigned yet.</param>
/// <param name="innerException">The exception that prevented the send, if any.</param>
[PublicAPI]
public sealed class RequestNotSentException(string message, string? requestId = null, Exception? innerException = null)
    : RequestReplyException(message, requestId, innerException)
{
    /// <summary>
    /// Gets whether the request was refused because the requesting host is stopping. A consumer whose own host stops
    /// while it sends a request treats this as shutdown rather than as a failure of the message it consumes.
    /// </summary>
    internal bool IsRequesterStopping { get; init; }
}
