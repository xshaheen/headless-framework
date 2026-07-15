// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Kafka;
using Headless.Testing.Tests;

namespace Tests;

public sealed class KafkaMessagingHeadersTests : TestBase
{
    [Fact]
    public void should_have_expected_value_when_kafka_key()
    {
        // given, when, then
        KafkaMessagingHeaders.KafkaKey.Should().Be("headless-kafka-key");
    }
}
