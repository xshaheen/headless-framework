// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Primitives;

namespace Headless;

/// <summary>
/// An exception that signals the caller spent a rate or attempt budget and must wait before trying again. Maps to
/// HTTP 429 Too Many Requests, with a <c>Retry-After</c> header, in the API exception handler.
/// </summary>
/// <remarks>
/// Derive from this class to give an exhausted budget its own type that application code can catch by name. The API
/// exception handler maps every derived type to the same 429 response as <see cref="TooManyRequestsException"/>, so the
/// derived type needs no exception handler of its own. Pass the wait, the descriptor, and your own message through the
/// protected constructor.
/// </remarks>
[PublicAPI]
public class TooManyRequestsException : Exception
{
    /// <summary>Initializes a too-many-requests exception.</summary>
    /// <param name="retryAfter">How long the caller must wait before the budget reopens.</param>
    /// <param name="error">
    /// Optional descriptor naming the exhausted budget; <see langword="null"/> leaves the response with the generic 429
    /// title and detail only.
    /// </param>
    public TooManyRequestsException(TimeSpan retryAfter, ErrorDescriptor? error = null)
        : base(error is null ? "Too many requests." : $"Too many requests: {error}")
    {
        RetryAfter = retryAfter;
        Error = error;
    }

    /// <summary>Initializes a derived too-many-requests exception with its own message.</summary>
    /// <param name="message">The exception message, for logs; it never reaches the response.</param>
    /// <param name="retryAfter">How long the caller must wait before the budget reopens.</param>
    /// <param name="error">
    /// Optional descriptor naming the exhausted budget; <see langword="null"/> leaves the response with the generic 429
    /// title and detail only.
    /// </param>
    /// <param name="innerException">The exception that caused this failure, if any.</param>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is <see langword="null"/>.</exception>
    protected TooManyRequestsException(
        string message,
        TimeSpan retryAfter,
        ErrorDescriptor? error,
        Exception? innerException = null
    )
        : base(Argument.IsNotNull(message), innerException)
    {
        RetryAfter = retryAfter;
        Error = error;
    }

    /// <summary>How long the caller must wait before the budget reopens.</summary>
    public TimeSpan RetryAfter { get; }

    /// <summary>The descriptor naming the exhausted budget, when the thrower supplied one.</summary>
    public ErrorDescriptor? Error { get; }
}
