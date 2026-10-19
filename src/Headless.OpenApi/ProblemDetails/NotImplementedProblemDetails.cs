// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Headless.OpenApi;

/// <summary>Problem details schema for 501 Not Implemented responses.</summary>
public sealed class NotImplementedProblemDetails : HeadlessProblemDetails
{
    /// <summary>The stable descriptor identifying what is not implemented, when one was supplied.</summary>
    public ErrorDescriptor? Error { get; init; }
}
