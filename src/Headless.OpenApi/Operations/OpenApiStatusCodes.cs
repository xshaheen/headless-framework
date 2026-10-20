// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.OpenApi;

internal static class OpenApiStatusCodes
{
    public const string BadRequest = "400";
    public const string Unauthorized = "401";
    public const string Forbidden = "403";
    public const string NotFound = "404";
    public const string RequestTimeout = "408";
    public const string Conflict = "409";
    public const string PayloadTooLarge = "413";
    public const string UnprocessableEntity = "422";
    public const string PreconditionRequired = "428";
    public const string TooManyRequests = "429";
    public const string InternalServerError = "500";
    public const string NotImplemented = "501";
    public const string ServiceUnavailable = "503";

    // OpenAPI range keys: they match any status in the class that the operation does not list exactly.
    public const string ClientErrorRange = "4XX";
    public const string ServerErrorRange = "5XX";
}
