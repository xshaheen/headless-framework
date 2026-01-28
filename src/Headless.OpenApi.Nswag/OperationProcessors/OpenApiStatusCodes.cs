// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Api.OperationProcessors;

internal static class OpenApiStatusCodes
{
    public const string BadRequest = "400";
    public const string Unauthorized = "401";
    public const string Forbidden = "403";
    public const string NotFound = "404";
    public const string Conflict = "409";
    public const string UnprocessableEntity = "422";
    public const string TooManyRequests = "429";
}
