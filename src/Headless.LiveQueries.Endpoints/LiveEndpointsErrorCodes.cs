// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.LiveQueries;

/// <summary>
/// The machine-readable error codes live endpoints put in a problem-details body, on a refused streaming request
/// and in the failure event that ends a stream. Clients branch on these constants; the description beside a code
/// is localized.
/// </summary>
[PublicAPI]
public static class LiveEndpointsErrorCodes
{
    /// <summary>
    /// A run of the endpoint recorded no dependency, so no change could ever update the stream. Sent with 500 when
    /// the stream is opened, and in the failure event when a later run recorded none.
    /// </summary>
    public const string NoDependencies = "g:live_query_no_dependencies";

    /// <summary>
    /// A run after the first one threw or answered with a status outside 200 to 299. Sent in the failure event,
    /// whose <c>status</c> is the run's status, or 500 for an exception.
    /// </summary>
    public const string RunFailed = "g:live_query_run_failed";

    /// <summary>
    /// A run answered 200 to 299 with a body whose content type is not JSON (<c>application/json</c> or a
    /// <c>+json</c> type), such as the <c>text/plain</c> of a handler that returns a string. Sent with 500 when the
    /// stream is opened, and in the failure event when a later run did so.
    /// </summary>
    public const string ResponseNotJson = "g:live_query_response_not_json";

    /// <summary>
    /// A run after the first one found the caller no longer authenticated or no longer authorized for the endpoint.
    /// Sent in the failure event, with <c>status</c> 401 or 403.
    /// </summary>
    public const string Unauthorized = "g:live_query_unauthorized";

    /// <summary>
    /// The streaming request was refused with 429 because the caller, or the host, already holds as many streams
    /// as <see cref="LiveEndpointsOptions" /> allows.
    /// </summary>
    public const string StreamLimitReached = "g:live_query_stream_limit_reached";
}
