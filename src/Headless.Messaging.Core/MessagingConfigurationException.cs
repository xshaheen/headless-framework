// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>Thrown when messaging configuration is invalid at startup.</summary>
[PublicAPI]
public sealed class MessagingConfigurationException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="MessagingConfigurationException"/> class.</summary>
    public MessagingConfigurationException(string message)
        : base(message) { }

    /// <summary>Initializes a new instance of the <see cref="MessagingConfigurationException"/> class.</summary>
    public MessagingConfigurationException(string message, Exception innerException)
        : base(message, innerException) { }
}
