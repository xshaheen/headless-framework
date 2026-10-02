// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>Thrown for a pending request when the requesting host stops before its reply arrives.</summary>
/// <remarks>
/// The request was sent, so the responder may still complete the work. This is not reported as
/// <see cref="OperationCanceledException"/>, because the caller did not cancel the call.
/// </remarks>
/// <param name="requestId">The request's identifier.</param>
[PublicAPI]
public sealed class RequestAbortedException(string? requestId)
    : RequestReplyException($"The requester stopped before the reply to request '{requestId}' arrived.", requestId);
