// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Headless.Constants;

[PublicAPI]
public static class HeadlessProblemDetailsConstants
{
    public static class Types
    {
        public const string EndpointNotFound = "https://tools.ietf.org/html/rfc9110#section-15.5.5";
        public const string EntityNotFound = "https://tools.ietf.org/html/rfc9110#section-15.5.5";
        public const string BadRequest = "https://tools.ietf.org/html/rfc9110#section-15.5.1";
        public const string Unauthorized = "https://tools.ietf.org/html/rfc9110#section-15.5.2";
        public const string Forbidden = "https://tools.ietf.org/html/rfc9110#section-15.5.4";
        public const string UnprocessableEntity = "https://tools.ietf.org/html/rfc4918#section-11.2";
        public const string Conflict = "https://tools.ietf.org/html/rfc9110#section-15.5.10";
        public const string InternalError = "https://tools.ietf.org/html/rfc9110#section-15.6.1";
        public const string TooManyRequests = "https://datatracker.ietf.org/doc/html/rfc6585#section-4";
        public const string RequestTimeout = "https://tools.ietf.org/html/rfc9110#section-15.5.9";
        public const string NotImplemented = "https://tools.ietf.org/html/rfc9110#section-15.6.2";
    }

    public static class Errors
    {
        public static ErrorDescriptor TenantContextRequired { get; } =
            new(
                code: "g:tenant-required",
                description: Details.TenantContextRequired,
                severity: ValidationSeverity.Error
            );

        public static ErrorDescriptor CrossTenantWrite { get; } =
            new(
                code: "g:cross-tenant-write",
                description: Details.CrossTenantWrite,
                severity: ValidationSeverity.Error
            );
    }

    public static class Titles
    {
        public const string EndpointNotFound = "endpoint-not-found";
        public const string EntityNotFound = "entity-not-found";
        public const string BadRequest = "bad-request";
        public const string Unauthorized = "unauthorized";
        public const string Forbidden = "forbidden";
        public const string UnprocessableEntity = "validation-problem";
        public const string Conflict = "conflict-request";
        public const string InternalError = "unhandled-exception";
        public const string TooManyRequests = "too-many-requests";
        public const string RequestTimeout = "request-timeout";
        public const string NotImplemented = "not-implemented";
    }

    public static class Details
    {
        public static string EndpointNotFound(string endpoint)
        {
            return $"The requested endpoint '{endpoint}' was not found.";
        }

        public const string EntityNotFound = "The requested resource was not found.";

        public const string BadRequest =
            "Failed to parse. The request body is empty or could not be understood by the server due to malformed syntax.";

        public const string Unauthorized = "You are unauthenticated to access this resource.";
        public const string Forbidden = "You are forbidden from accessing this resource.";
        public const string Conflict = "Conflict - one or more business rules violated.";
        public const string UnprocessableEntity = "One or more validation errors occurred.";
        public const string InternalError = "An error occurred while processing your request.";
        public const string TooManyRequests = "Too many requests - please try again later.";
        public const string RequestTimeout = "The request timed out.";
        public const string NotImplemented = "This functionality is not implemented.";

        public const string TenantContextRequired = "An operation required an ambient tenant context but none was set.";
        public const string CrossTenantWrite = "Tenant-owned write does not match the current tenant context.";
    }
}
