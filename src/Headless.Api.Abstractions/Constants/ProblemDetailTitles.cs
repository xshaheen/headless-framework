// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Constants;

/// <summary>
/// Well-known <c>ProblemDetails</c> constants (RFC 9457) used across Headless API error responses.
/// </summary>
/// <remarks>
/// The nested classes mirror the fields of a <c>ProblemDetails</c> document:
/// <list type="bullet">
///   <item><description><see cref="Types"/> — RFC-anchored <c>type</c> URIs identifying the problem category.</description></item>
///   <item><description><see cref="Titles"/> — short human-readable <c>title</c> strings (kebab-case, stable across responses).</description></item>
///   <item><description><see cref="Details"/> — default human-readable <c>detail</c> strings (may be localised).</description></item>
///   <item><description><see cref="Errors"/> — framework-level <c>ErrorDescriptor</c> instances for structured error payloads.</description></item>
/// </list>
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
    }

    /// <summary>Pre-built <c>ErrorDescriptor</c> instances for framework-level error conditions.</summary>
    public static class Errors
    {
        /// <summary>
        /// Error descriptor for operations that require an ambient tenant context when none has been set.
        /// Error code: <c>g:tenant_required</c>.
        /// </summary>
        public static ErrorDescriptor TenantContextRequired { get; } =
            new(
                code: "g:tenant_required",
                description: Details.TenantContextRequired,
                severity: ValidationSeverity.Error
            );

        /// <summary>
        /// Error descriptor for writes where the target entity's tenant does not match the current tenant context.
        /// Error code: <c>g:cross_tenant_write</c>.
        /// </summary>
        public static ErrorDescriptor CrossTenantWrite { get; } =
            new(
                code: "g:cross_tenant_write",
                description: Details.CrossTenantWrite,
                severity: ValidationSeverity.Error
            );
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
    }

    /// <summary>Default human-readable <c>detail</c> strings for <c>ProblemDetails</c> responses.</summary>
    /// <remarks>These strings may be localised or overridden by the hosting application.</remarks>
    public static class Details
    {
        /// <summary>Returns a detail message indicating the named endpoint was not found.</summary>
        /// <param name="endpoint">The endpoint path or name that was requested.</param>
        /// <returns>A formatted detail string including <paramref name="endpoint"/>.</returns>
        public static string EndpointNotFound(string endpoint)
        {
            return $"The requested endpoint '{endpoint}' was not found.";
        }

        /// <summary>Detail for 404 responses when the requested resource does not exist.</summary>
        public const string EntityNotFound = "The requested resource was not found.";

        /// <summary>Detail for 400 responses when the request body is absent or malformed.</summary>
        public const string BadRequest =
            "Failed to parse. The request body is empty or could not be understood by the server due to malformed syntax.";

        /// <summary>Detail for 401 responses when the caller is not authenticated.</summary>
        public const string Unauthorized = "You are unauthenticated to access this resource.";

        /// <summary>Detail for 403 responses when the caller lacks permission.</summary>
        public const string Forbidden = "You are forbidden from accessing this resource.";

        /// <summary>Detail for 409 responses when a business rule is violated.</summary>
        public const string Conflict = "Conflict - one or more business rules violated.";

        /// <summary>Detail for 422 responses when input validation fails.</summary>
        public const string UnprocessableEntity = "One or more validation errors occurred.";

        /// <summary>Detail for 500 responses for unhandled exceptions.</summary>
        public const string InternalError = "An error occurred while processing your request.";

        /// <summary>Detail for 429 responses when rate limits are exceeded.</summary>
        public const string TooManyRequests = "Too many requests - please try again later.";

        /// <summary>Detail for 408 responses when the request exceeded the allowed time.</summary>
        public const string RequestTimeout = "The request timed out.";

        /// <summary>Detail for 501 responses when the feature is not implemented.</summary>
        public const string NotImplemented = "This functionality is not implemented.";

        /// <summary>Detail for 413 responses when the request body exceeds the size limit.</summary>
        public const string PayloadTooLarge = "The request payload is too large to process.";

        /// <summary>Detail for operations that required a tenant context that was not set.</summary>
        public const string TenantContextRequired = "An operation required an ambient tenant context but none was set.";

        /// <summary>Detail for writes where the target entity's tenant does not match the current tenant context.</summary>
        public const string CrossTenantWrite = "Tenant-owned write does not match the current tenant context.";
    }
}
