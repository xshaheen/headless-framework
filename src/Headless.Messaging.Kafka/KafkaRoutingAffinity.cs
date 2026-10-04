// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Kafka;

internal static class KafkaRoutingAffinity
{
    internal static readonly MessagingRoutingAffinityMapping Mapping = new(KafkaMessagingHeaders.KafkaKey);
}
