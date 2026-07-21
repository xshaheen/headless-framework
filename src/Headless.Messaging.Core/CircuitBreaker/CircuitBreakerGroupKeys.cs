// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Internal;
using Headless.Messaging.Messages;

namespace Headless.Messaging.CircuitBreaker;

internal static class CircuitBreakerGroupKeys
{
    internal static string For(MessageLane lane, string groupName)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{(short)lane}:{groupName}");
    }

    internal static string For(IntentType intentType, string groupName)
    {
        return For(MessageLaneCompatibility.ToLane(intentType), groupName);
    }

    internal static string For(ConsumerMetadata metadata)
    {
        return For(metadata.Lane, metadata.Group!);
    }

    internal static string For(MediumMessage message)
    {
        return For(message.Lane, message.Origin.GetGroup()!);
    }
}
