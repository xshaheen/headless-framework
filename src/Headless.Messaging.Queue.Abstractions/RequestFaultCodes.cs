// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Stable codes a fault reply carries, exposed on <see cref="RequestFaultedException.Code"/>. The values are wire
/// contract and never change.
/// </summary>
[PublicAPI]
public static class RequestFaultCodes
{
    /// <summary>The responder ran and failed terminally before the request's deadline.</summary>
    public const string HandlerFailed = "handler_failed";

    /// <summary>
    /// The request reached a consumer that does not respond to requests. The responder host committed and skipped it,
    /// so no work ran.
    /// </summary>
    public const string NoResponder = "no_responder";

    /// <summary>
    /// The responder host rejected the request on arrival, for example because its contract version did not match or
    /// its body could not be deserialized. No work ran.
    /// </summary>
    public const string RequestRejected = "request_rejected";

    /// <summary>The responder returned <see langword="null"/> instead of a response.</summary>
    public const string NullResponse = "null_response";
}
