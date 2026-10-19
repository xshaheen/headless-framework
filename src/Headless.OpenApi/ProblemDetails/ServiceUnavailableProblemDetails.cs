// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Headless.OpenApi;

/// <summary>Problem details schema for 503 Service Unavailable responses.</summary>
public sealed class ServiceUnavailableProblemDetails : HeadlessProblemDetails
{
    /// <summary>
    /// The number of seconds after which the client may retry the request, present only when the server knows it.
    /// </summary>
    public int? RetryAfter { get; init; }

    /// <summary>The stable descriptor identifying why the service is unavailable, when one was supplied.</summary>
    public ErrorDescriptor? Error { get; init; }
}
