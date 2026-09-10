// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Headless.OpenApi.Nswag.Models;

/// <summary>
/// Problem details schema for 429 Too Many Requests responses.
/// </summary>
public sealed class TooManyRequestsProblemDetails : HeadlessProblemDetails
{
    /// <summary>The number of seconds after which the client may retry the request.</summary>
    public required int RetryAfter { get; init; }

    /// <summary>The stable descriptor identifying which limit was exceeded, when one was supplied.</summary>
    public ErrorDescriptor? Error { get; init; }
}
