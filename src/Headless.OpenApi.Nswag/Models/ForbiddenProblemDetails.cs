// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Headless.OpenApi.Nswag.Models;

/// <summary>
/// Problem details schema for 403 Forbidden responses.
/// </summary>
public sealed class ForbiddenProblemDetails : HeadlessProblemDetails
{
    /// <summary>
    /// The stable descriptor identifying why access was denied, when one was supplied. The framework emits
    /// <c>g:tenant_required</c> here for a request that resolved no tenant; applications may supply their own.
    /// </summary>
    public ErrorDescriptor? Error { get; init; }
}
