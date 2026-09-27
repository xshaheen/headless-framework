// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Headless.Exceptions;

/// <summary>
/// An exception that signals the caller spent a rate or attempt budget and must wait before trying again. Maps to
/// HTTP 429 Too Many Requests, with a <c>Retry-After</c> header, in the API exception handler.
/// </summary>
/// <param name="retryAfter">How long the caller must wait before the budget reopens.</param>
/// <param name="error">
/// Optional descriptor naming the exhausted budget; <see langword="null"/> leaves the response with the generic 429
/// title and detail only.
/// </param>
[PublicAPI]
public sealed class TooManyRequestsException(TimeSpan retryAfter, ErrorDescriptor? error = null)
    : Exception(error is null ? "Too many requests." : $"Too many requests: {error}")
{
    /// <summary>How long the caller must wait before the budget reopens.</summary>
    public TimeSpan RetryAfter { get; } = retryAfter;

    /// <summary>The descriptor naming the exhausted budget, when the thrower supplied one.</summary>
    public ErrorDescriptor? Error { get; } = error;
}
