// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Confluent.Kafka;
using Headless.Messaging.Kafka;
using Headless.Testing.Tests;

namespace Tests;

public sealed class KafkaBrokerFailureTests : TestBase
{
    [Theory]
    [InlineData(ErrorCode.Local_Transport)]
    [InlineData(ErrorCode.Local_AllBrokersDown)]
    [InlineData(ErrorCode.Local_MsgTimedOut)]
    [InlineData(ErrorCode.BrokerNotAvailable)]
    public void should_count_a_delivery_that_found_no_broker(ErrorCode code)
    {
        KafkaTransport.IsBrokerUnreachable(new KafkaException(new Error(code))).Should().BeTrue();
    }

    [Theory]
    [InlineData(ErrorCode.MsgSizeTooLarge)]
    [InlineData(ErrorCode.TopicAuthorizationFailed)]
    [InlineData(ErrorCode.UnknownTopicOrPart)]
    public void should_not_count_a_message_the_broker_refused(ErrorCode code)
    {
        KafkaTransport.IsBrokerUnreachable(new KafkaException(new Error(code))).Should().BeFalse();
    }
}
