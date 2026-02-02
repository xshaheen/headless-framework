// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Exception thrown when a message fails to be sent to the transport.
/// </summary>
public sealed class PublisherSentFailedException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PublisherSentFailedException"/> class.
    /// </summary>
    public PublisherSentFailedException() { }

    /// <summary>
    /// Initializes a new instance of the <see cref="PublisherSentFailedException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public PublisherSentFailedException(string message)
        : base(message) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="PublisherSentFailedException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public PublisherSentFailedException(string message, Exception? innerException)
        : base(message, innerException) { }
}
