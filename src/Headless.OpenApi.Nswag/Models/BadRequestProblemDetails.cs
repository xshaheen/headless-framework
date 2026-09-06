// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Headless.OpenApi.Nswag.Models;

/// <summary>
/// Problem details schema for 400 Bad Request responses.
/// </summary>
public sealed class BadRequestProblemDetails : HeadlessProblemDetails
{
    /// <summary>The stable descriptor identifying why the request was rejected, when one was supplied.</summary>
    public ErrorDescriptor? Error { get; init; }
}
