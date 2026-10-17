// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Headless.OpenApi;

/// <summary>Problem details schema for 408 Request Timeout responses.</summary>
public sealed class RequestTimeoutProblemDetails : HeadlessProblemDetails
{
    /// <summary>The stable descriptor identifying what timed out, when one was supplied.</summary>
    public ErrorDescriptor? Error { get; init; }
}
