// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching;

/// <summary>
/// Thrown when a cache entry exceeds <see cref="InMemoryCacheOptions.MaxEntrySize"/> and
/// <see cref="InMemoryCacheOptions.ShouldThrowOnMaxEntrySizeExceeded"/> is <see langword="true"/>.
/// When <see cref="InMemoryCacheOptions.ShouldThrowOnMaxEntrySizeExceeded"/> is <see langword="false"/> (the
/// default) the write is silently dropped instead.
/// </summary>
[PublicAPI]
public sealed class MaxEntrySizeExceededException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="MaxEntrySizeExceededException"/> class.</summary>
    public MaxEntrySizeExceededException() { }

    /// <summary>Initializes a new instance of the <see cref="MaxEntrySizeExceededException"/> class with a message.</summary>
    /// <param name="message">The exception message.</param>
    public MaxEntrySizeExceededException(string message)
        : base(message) { }

    /// <summary>Initializes a new instance of the <see cref="MaxEntrySizeExceededException"/> class with a message and inner exception.</summary>
    /// <param name="message">The exception message.</param>
    /// <param name="innerException">The exception that is the cause of this exception.</param>
    public MaxEntrySizeExceededException(string message, Exception innerException)
        : base(message, innerException) { }
}
