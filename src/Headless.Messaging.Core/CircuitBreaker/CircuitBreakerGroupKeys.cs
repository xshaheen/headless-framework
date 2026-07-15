// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;

namespace Headless.Messaging.CircuitBreaker;

internal static class CircuitBreakerGroupKeys
{
    internal static string For(IntentType intentType, string groupName)
    {
        return $"{intentType:D}:{groupName}";
    }

    internal static string For(ConsumerMetadata metadata)
    {
        return For(metadata.IntentType, metadata.Group!);
    }

    internal static string For(MediumMessage message)
    {
        return For(message.IntentType, message.Origin.GetGroup()!);
    }
}
