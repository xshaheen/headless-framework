// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Builds the lane-qualified circuit key of a consumer. A circuit belongs to one consumer identity on one lane, so the
/// same identity on the Bus and Queue lanes trips independently.
/// </summary>
internal static class CircuitBreakerKeys
{
    internal static string For(MessageLane lane, string consumerIdentity)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{(short)lane}:{consumerIdentity}");
    }

    internal static string For(ConsumerMetadata metadata)
    {
        return For(metadata.Lane, metadata.ConsumerIdentity);
    }

    internal static string For(ConsumerExecutorDescriptor descriptor)
    {
        return descriptor.CircuitBreakerKey;
    }

    internal static string For(MediumMessage message)
    {
        return For(message.Lane, message.Origin.GetConsumerIdentity()!);
    }
}
