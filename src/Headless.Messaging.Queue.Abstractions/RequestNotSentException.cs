// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Thrown when a request never left the caller: publish middleware suppressed it, the requester was stopping, or its
/// reply listener did not become ready within the timeout.
/// </summary>
/// <remarks>No responder can have received the request, so retrying the call cannot repeat work.</remarks>
/// <param name="message">Why the request was not sent.</param>
/// <param name="requestId">The request's identifier, or <see langword="null"/> when none was assigned yet.</param>
/// <param name="innerException">The exception that prevented the send, if any.</param>
[PublicAPI]
public sealed class RequestNotSentException(string message, string? requestId = null, Exception? innerException = null)
    : RequestReplyException(message, requestId, innerException);
