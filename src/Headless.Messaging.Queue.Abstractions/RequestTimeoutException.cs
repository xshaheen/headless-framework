// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>Thrown when no reply arrives before the request's timeout.</summary>
/// <remarks>
/// A timeout is ambiguous: the responder may have completed the work after the caller stopped waiting, or the reply
/// may have been lost after the work became durable.
/// </remarks>
/// <param name="requestId">The request's identifier.</param>
/// <param name="timeout">How long the caller waited.</param>
[PublicAPI]
public sealed class RequestTimeoutException(string? requestId, TimeSpan timeout)
    : RequestReplyException($"No reply arrived within {timeout} for request '{requestId}'.", requestId)
{
    /// <summary>Gets how long the caller waited for the reply.</summary>
    public TimeSpan Timeout { get; } = timeout;
}
