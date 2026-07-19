// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Exceptions;

/// <summary>
/// Thrown when a message is dispatched to a subscriber that has not been registered with the consumer registry.
/// </summary>
[PublicAPI]
public sealed class SubscriberNotFoundException(string message) : Exception(message);
