// Copyright (c) Mahmoud Shaheen. All rights reserved.

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
    }
}
