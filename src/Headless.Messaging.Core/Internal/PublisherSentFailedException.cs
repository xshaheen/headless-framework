// Copyright (c) Mahmoud Shaheen. All rights reserved.

// This type has been moved to Headless.Messaging.Abstractions.
// This file exists only for backwards compatibility with code using the Internal namespace.
// New code should use Headless.Messaging.PublisherSentFailedException directly.

using Headless.Messaging;

namespace Headless.Messaging.Internal;

/// <summary>
/// Backwards compatibility alias. Use <see cref="Headless.Messaging.PublisherSentFailedException"/> instead.
/// </summary>
[Obsolete("Use Headless.Messaging.PublisherSentFailedException instead.")]
public class PublisherSentFailedException : Messaging.PublisherSentFailedException
{
    /// <inheritdoc />
    public PublisherSentFailedException(string message)
        : base(message) { }

    /// <inheritdoc />
    public PublisherSentFailedException(string message, Exception? innerException)
        : base(message, innerException) { }
}
