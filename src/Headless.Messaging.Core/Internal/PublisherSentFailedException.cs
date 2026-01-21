// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Internal;

public class PublisherSentFailedException : Exception
{
    public PublisherSentFailedException(string message)
        : base(message) { }

    public PublisherSentFailedException(string message, Exception? ex)
        : base(message, ex) { }
}
