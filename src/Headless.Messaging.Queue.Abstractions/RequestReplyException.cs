// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Base type of the request/reply outcomes an <see cref="IRequestClient"/> call fails with, so a caller can catch all of
/// them at once. Two failures fall outside it: the caller's own cancellation surfaces as
/// <see cref="OperationCanceledException"/>, and a response body that cannot be read as the response type surfaces as
/// <c>MessageDeserializationException</c>.
/// </summary>
/// <param name="message">A description of the failure.</param>
/// <param name="requestId">The request's identifier, or <see langword="null"/> when none was assigned yet.</param>
/// <param name="innerException">The exception that caused the failure, if any.</param>
[PublicAPI]
public abstract class RequestReplyException(string message, string? requestId, Exception? innerException = null)
    : Exception(message, innerException)
{
    /// <summary>
    /// Gets the framework-generated request identifier carried in <see cref="Headers.RequestId"/>, or
    /// <see langword="null"/> when the call failed before one was assigned.
    /// </summary>
    public string? RequestId { get; } = requestId;
}
