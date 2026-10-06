// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Api;

/// <summary>
/// Well-known <c>ProblemDetails</c> constants (RFC 9457) used across Headless API error responses.
/// </summary>
/// <remarks>
/// <para>
/// These are the machine-readable parts of a <c>ProblemDetails</c> document, and they never vary by
/// culture:
/// </para>
/// <list type="bullet">
///   <item><description><see cref="Types"/> — RFC-anchored <c>type</c> URIs identifying the problem category.</description></item>
///   <item><description><see cref="Titles"/> — kebab-case <c>title</c> identifiers, stable across responses and releases.</description></item>
/// </list>
/// <para>
/// The default <c>detail</c> text is not a constant: <c>IProblemDetailsCreator</c> resolves it per
/// response under the current UI culture. Clients branch on <c>type</c>, <c>title</c>, and the error
/// <c>code</c>, never on <c>detail</c>.
/// </para>
/// </remarks>
[PublicAPI]
public static class HeadlessProblemDetailsConstants
{
    /// <summary>RFC-anchored URIs used as the <c>type</c> field in <c>ProblemDetails</c> responses.</summary>
    public static class Types
    {
        /// <summary>RFC 9110 §15.5.5 — 404 Not Found (endpoint not matched).</summary>
        public const string EndpointNotFound = "https://tools.ietf.org/html/rfc9110#section-15.5.5";

        /// <summary>RFC 9110 §15.5.5 — 404 Not Found (resource does not exist).</summary>
        public const string EntityNotFound = "https://tools.ietf.org/html/rfc9110#section-15.5.5";

        /// <summary>RFC 9110 §15.5.1 — 400 Bad Request.</summary>
        public const string BadRequest = "https://tools.ietf.org/html/rfc9110#section-15.5.1";

        /// <summary>RFC 9110 §15.5.2 — 401 Unauthorized (unauthenticated).</summary>
        public const string Unauthorized = "https://tools.ietf.org/html/rfc9110#section-15.5.2";

        /// <summary>RFC 9110 §15.5.4 — 403 Forbidden.</summary>
        public const string Forbidden = "https://tools.ietf.org/html/rfc9110#section-15.5.4";

        /// <summary>RFC 4918 §11.2 — 422 Unprocessable Entity (validation failures).</summary>
        public const string UnprocessableEntity = "https://tools.ietf.org/html/rfc4918#section-11.2";

        /// <summary>RFC 9110 §15.5.10 — 409 Conflict (business rule violation).</summary>
        public const string Conflict = "https://tools.ietf.org/html/rfc9110#section-15.5.10";

        /// <summary>RFC 9110 §15.6.1 — 500 Internal Server Error.</summary>
        public const string InternalError = "https://tools.ietf.org/html/rfc9110#section-15.6.1";

        /// <summary>RFC 6585 §4 — 429 Too Many Requests.</summary>
        public const string TooManyRequests = "https://datatracker.ietf.org/doc/html/rfc6585#section-4";

        /// <summary>RFC 9110 §15.5.9 — 408 Request Timeout.</summary>
        public const string RequestTimeout = "https://tools.ietf.org/html/rfc9110#section-15.5.9";

        /// <summary>RFC 9110 §15.6.2 — 501 Not Implemented.</summary>
        public const string NotImplemented = "https://tools.ietf.org/html/rfc9110#section-15.6.2";

        /// <summary>RFC 9110 §15.5.14 — 413 Content Too Large.</summary>
        public const string PayloadTooLarge = "https://tools.ietf.org/html/rfc9110#section-15.5.14";

        /// <summary>RFC 9110 §15.6.4 — 503 Service Unavailable.</summary>
        public const string ServiceUnavailable = "https://tools.ietf.org/html/rfc9110#section-15.6.4";
    }

    /// <summary>Short, stable <c>title</c> strings (kebab-case) for <c>ProblemDetails</c> responses.</summary>
    /// <remarks>Titles are intended to be stable across releases; do not localise them.</remarks>
    public static class Titles
    {
        /// <summary>Title for 404 responses when the requested endpoint was not matched.</summary>
        public const string EndpointNotFound = "endpoint-not-found";

        /// <summary>Title for 404 responses when the requested resource does not exist.</summary>
        public const string EntityNotFound = "entity-not-found";

        /// <summary>Title for 400 Bad Request responses.</summary>
        public const string BadRequest = "bad-request";

        /// <summary>Title for 401 Unauthorized responses.</summary>
        public const string Unauthorized = "unauthorized";

        /// <summary>Title for 403 Forbidden responses.</summary>
        public const string Forbidden = "forbidden";

        /// <summary>Title for 422 Unprocessable Entity (validation) responses.</summary>
        public const string UnprocessableEntity = "validation-problem";

        /// <summary>Title for 409 Conflict responses.</summary>
        public const string Conflict = "conflict-request";

        /// <summary>Title for 500 Internal Server Error responses.</summary>
        public const string InternalError = "unhandled-exception";

        /// <summary>Title for 429 Too Many Requests responses.</summary>
        public const string TooManyRequests = "too-many-requests";

        /// <summary>Title for 408 Request Timeout responses.</summary>
        public const string RequestTimeout = "request-timeout";

        /// <summary>Title for 501 Not Implemented responses.</summary>
        public const string NotImplemented = "not-implemented";

        /// <summary>Title for 413 Payload Too Large responses.</summary>
        public const string PayloadTooLarge = "payload-too-large";

        /// <summary>Title for 503 Service Unavailable responses.</summary>
        public const string ServiceUnavailable = "service-unavailable";
    }
}
