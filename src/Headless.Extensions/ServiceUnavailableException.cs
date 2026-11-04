// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Headless;

/// <summary>
/// An exception that signals a dependency the request needs (a counter store, a cache, a remote service) cannot answer
/// now, so the same request can succeed once it recovers. Maps to HTTP 503 Service Unavailable with
/// <c>Cache-Control: no-store</c> in the API exception handler.
/// </summary>
/// <remarks>
/// The message is logged, never written to the response, but a store's own exception text can name a host, a key, or
/// a connection string, so a derived exception states the failure in its own words and keeps the store's exception
/// only as <see cref="Exception.InnerException"/>. No <c>Retry-After</c> is sent: nothing says when the dependency
/// recovers, and an invented value would have clients synchronize their retries on it.
/// </remarks>
/// <param name="message">What is unavailable, in words that are safe to log.</param>
/// <param name="error">
/// Optional descriptor for the response; <see langword="null"/> leaves it with the generic 503 title and detail only.
/// </param>
/// <param name="innerException">The dependency's own failure, for the logs.</param>
[PublicAPI]
public class ServiceUnavailableException(
    string message,
    ErrorDescriptor? error = null,
    Exception? innerException = null
) : Exception(message, innerException)
{
    /// <summary>The descriptor for the response, when the thrower supplied one.</summary>
    public ErrorDescriptor? Error { get; } = error;
}
