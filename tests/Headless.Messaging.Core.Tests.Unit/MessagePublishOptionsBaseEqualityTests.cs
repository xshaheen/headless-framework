// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;

namespace Tests;

public sealed class MessagePublishOptionsBaseEqualityTests
{
    [Fact]
    public void should_compare_not_equal_when_only_message_type_differs()
    {
        // given
        var withType = new PublishOptions
        {
            MessageName = "orders.placed",
            CorrelationId = "corr-1",
            MessageType = typeof(SampleResponse),
        };
        var withoutType = withType with { MessageType = null };

        // when
        var areEqual = withType.Equals(withoutType);

        // then
        areEqual.Should().BeFalse("MessageType participates in equality so a captured response type is not lost");
        withType.GetHashCode().Should().NotBe(withoutType.GetHashCode());
    }

    [Fact]
    public void should_compare_equal_when_message_type_is_identical()
    {
        // given
        var first = new PublishOptions
        {
            MessageName = "orders.placed",
            CorrelationId = "corr-1",
            MessageType = typeof(SampleResponse),
        };
        var second = new PublishOptions
        {
            MessageName = "orders.placed",
            CorrelationId = "corr-1",
            MessageType = typeof(SampleResponse),
        };

        // when & then
        first.Should().Be(second);
        first.GetHashCode().Should().Be(second.GetHashCode());
    }

    [Fact]
    public void should_compare_equal_when_message_type_is_null_on_both()
    {
        // given (external callers always leave MessageType null)
        var first = new PublishOptions { MessageName = "orders.placed", CorrelationId = "corr-1" };
        var second = new PublishOptions { MessageName = "orders.placed", CorrelationId = "corr-1" };

        // when & then
        first.Should().Be(second);
        first.GetHashCode().Should().Be(second.GetHashCode());
    }

    private sealed record SampleResponse(string Status);
}
