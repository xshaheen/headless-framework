// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Messages;
using Headless.Testing.Tests;

namespace Tests;

public sealed class ConsumeContextTests : TestBase
{
    [Fact]
    public void should_create_context_with_valid_properties()
    {
        // given
        var message = new TestMessage("order-123", 99.99m);
        var messageId = Faker.Random.Guid().ToString();
        var correlationId = Faker.Random.Guid().ToString();
        var timestamp = DateTimeOffset.UtcNow;
        var topic = "test.topic";
        var headers = new MessageHeader(
            new Dictionary<string, string?>(StringComparer.Ordinal) { ["custom-header"] = "custom-value" }
        );

        // when
        var context = new ConsumeContext<TestMessage>
        {
            Message = message,
            MessageId = messageId,
            CorrelationId = correlationId,
            Timestamp = timestamp,
            Topic = topic,
            Headers = headers,
        };

        // then
        context.Message.Should().Be(message);
        context.MessageId.Should().Be(messageId);
        context.CorrelationId.Should().Be(correlationId);
        context.Timestamp.Should().Be(timestamp);
        context.Topic.Should().Be(topic);
        context.Headers.Should().BeSameAs(headers);
    }

    [Fact]
    public void should_throw_when_messageId_is_null()
    {
        // given
        var message = new TestMessage("order-123", 99.99m);

        // when
        var act = () =>
            new ConsumeContext<TestMessage>
            {
                Message = message,
                MessageId = null!,
                CorrelationId = null,
                Timestamp = DateTimeOffset.UtcNow,
                Topic = "test.topic",
                Headers = new MessageHeader(new Dictionary<string, string?>(StringComparer.Ordinal)),
            };

        // then
        act.Should().Throw<ArgumentException>().WithMessage("*MessageId cannot be null or whitespace*");
    }

    [Fact]
    public void should_throw_when_messageId_is_empty_string()
    {
        // given
        var message = new TestMessage("order-123", 99.99m);

        // when
        var act = () =>
            new ConsumeContext<TestMessage>
            {
                Message = message,
                MessageId = "",
                CorrelationId = null,
                Timestamp = DateTimeOffset.UtcNow,
                Topic = "test.topic",
                Headers = new MessageHeader(new Dictionary<string, string?>(StringComparer.Ordinal)),
            };

        // then
        act.Should().Throw<ArgumentException>().WithMessage("*MessageId cannot be null or whitespace*");
    }

    [Fact]
    public void should_throw_when_messageId_is_whitespace()
    {
        // given
        var message = new TestMessage("order-123", 99.99m);

        // when
        var act = () =>
            new ConsumeContext<TestMessage>
            {
                Message = message,
                MessageId = "   ",
                CorrelationId = null,
                Timestamp = DateTimeOffset.UtcNow,
                Topic = "test.topic",
                Headers = new MessageHeader(new Dictionary<string, string?>(StringComparer.Ordinal)),
            };

        // then
        act.Should().Throw<ArgumentException>().WithMessage("*MessageId cannot be null or whitespace*");
    }

    [Fact]
    public void should_allow_null_correlationId()
    {
        // given
        var message = new TestMessage("order-123", 99.99m);

        // when
        var context = new ConsumeContext<TestMessage>
        {
            Message = message,
            MessageId = Faker.Random.Guid().ToString(),
            CorrelationId = null,
            Timestamp = DateTimeOffset.UtcNow,
            Topic = "test.topic",
            Headers = new MessageHeader(new Dictionary<string, string?>(StringComparer.Ordinal)),
        };

        // then
        context.CorrelationId.Should().BeNull();
    }

    [Fact]
    public void should_throw_when_correlationId_is_empty_string()
    {
        // given
        var message = new TestMessage("order-123", 99.99m);

        // when
        var act = () =>
            new ConsumeContext<TestMessage>
            {
                Message = message,
                MessageId = Faker.Random.Guid().ToString(),
                CorrelationId = "",
                Timestamp = DateTimeOffset.UtcNow,
                Topic = "test.topic",
                Headers = new MessageHeader(new Dictionary<string, string?>(StringComparer.Ordinal)),
            };

        // then
        act.Should().Throw<ArgumentException>().WithMessage("*CorrelationId cannot be an empty string*");
    }

    [Fact]
    public void should_throw_when_correlationId_is_whitespace()
    {
        // given
        var message = new TestMessage("order-123", 99.99m);

        // when
        var act = () =>
            new ConsumeContext<TestMessage>
            {
                Message = message,
                MessageId = Faker.Random.Guid().ToString(),
                CorrelationId = "   ",
                Timestamp = DateTimeOffset.UtcNow,
                Topic = "test.topic",
                Headers = new MessageHeader(new Dictionary<string, string?>(StringComparer.Ordinal)),
            };

        // then
        act.Should().Throw<ArgumentException>().WithMessage("*CorrelationId cannot be an empty string*");
    }

    [Fact]
    public void should_expose_message_payload()
    {
        // given
        var orderId = Faker.Random.Guid().ToString();
        var amount = Faker.Random.Decimal(1, 1000);
        var message = new TestMessage(orderId, amount);

        // when
        var context = _CreateContext(message);

        // then
        context.Message.OrderId.Should().Be(orderId);
        context.Message.Amount.Should().Be(amount);
    }

    [Fact]
    public void should_expose_headers()
    {
        // given
        var headers = new MessageHeader(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["custom-header"] = "custom-value",
                ["another-header"] = "another-value",
            }
        );

        // when
        var context = new ConsumeContext<TestMessage>
        {
            Message = new TestMessage("order-123", 99.99m),
            MessageId = Faker.Random.Guid().ToString(),
            CorrelationId = null,
            Timestamp = DateTimeOffset.UtcNow,
            Topic = "test.topic",
            Headers = headers,
        };

        // then
        context.Headers.Should().ContainKey("custom-header");
        context.Headers["custom-header"].Should().Be("custom-value");
        context.Headers.Should().ContainKey("another-header");
        context.Headers["another-header"].Should().Be("another-value");
    }

    [Fact]
    public void should_expose_timestamp()
    {
        // given
        var timestamp = new DateTimeOffset(2026, 1, 25, 12, 30, 0, TimeSpan.Zero);

        // when
        var context = new ConsumeContext<TestMessage>
        {
            Message = new TestMessage("order-123", 99.99m),
            MessageId = Faker.Random.Guid().ToString(),
            CorrelationId = null,
            Timestamp = timestamp,
            Topic = "test.topic",
            Headers = new MessageHeader(new Dictionary<string, string?>(StringComparer.Ordinal)),
        };

        // then
        context.Timestamp.Should().Be(timestamp);
    }

    [Fact]
    public void should_expose_topic()
    {
        // given
        var topic = "orders.placed.v2";

        // when
        var context = new ConsumeContext<TestMessage>
        {
            Message = new TestMessage("order-123", 99.99m),
            MessageId = Faker.Random.Guid().ToString(),
            CorrelationId = null,
            Timestamp = DateTimeOffset.UtcNow,
            Topic = topic,
            Headers = new MessageHeader(new Dictionary<string, string?>(StringComparer.Ordinal)),
        };

        // then
        context.Topic.Should().Be(topic);
    }

    private ConsumeContext<TestMessage> _CreateContext(TestMessage message)
    {
        return new ConsumeContext<TestMessage>
        {
            Message = message,
            MessageId = Faker.Random.Guid().ToString(),
            CorrelationId = null,
            Timestamp = DateTimeOffset.UtcNow,
            Topic = "test.topic",
            Headers = new MessageHeader(new Dictionary<string, string?>(StringComparer.Ordinal)),
        };
    }
}

public sealed record TestMessage(string OrderId, decimal Amount);
