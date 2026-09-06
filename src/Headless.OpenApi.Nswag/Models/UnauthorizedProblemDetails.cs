// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Headless.OpenApi.Nswag.Models;

/// <summary>
/// Problem details schema for 401 Unauthorized responses.
/// </summary>
public sealed class UnauthorizedProblemDetails : HeadlessProblemDetails
{
    /// <summary>The stable descriptor identifying why authentication failed, when one was supplied.</summary>
    public ErrorDescriptor? Error { get; init; }
}
