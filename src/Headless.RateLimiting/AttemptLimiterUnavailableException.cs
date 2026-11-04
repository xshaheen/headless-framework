// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.RateLimiting;

/// <summary>
/// Thrown when the attempt limiter's counter store fails, so the attempt can be neither counted nor refused. Maps to
/// HTTP 503 with <c>Cache-Control: no-store</c>, like every <see cref="ServiceUnavailableException"/>.
/// </summary>
/// <remarks>
/// The message names only the purpose. The store's exception, whose text can name the counter key or the server, is
/// kept as <see cref="Exception.InnerException"/> for the logs and never reaches the response.
/// </remarks>
/// <param name="purpose">The limiter purpose the caller passed.</param>
/// <param name="innerException">The counter store's failure.</param>
[PublicAPI]
public sealed class AttemptLimiterUnavailableException(string purpose, Exception innerException)
    : ServiceUnavailableException($"The attempt limiter for '{purpose}' is unavailable.", error: null, innerException)
{
    /// <summary>The limiter purpose whose counter could not be read or written.</summary>
    public string Purpose { get; } = purpose;
}
