// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
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
        const string messageName = "test.messageName";
        var headers = new MessageHeader(
            new Dictionary<string, string?>(StringComparer.Ordinal) { ["custom-header"] = "custom-value" }
        );

        // when
        var context = new ConsumeContext<TestMessage>
        {
            Lane = MessageLane.Bus,
            Message = message,
            MessageId = messageId,
            CorrelationId = correlationId,
            Timestamp = timestamp,
            MessageName = messageName,
            Headers = headers,
        };

        // then
        context.Message.Should().Be(message);
        context.MessageId.Should().Be(messageId);
        context.CorrelationId.Should().Be(correlationId);
        context.Timestamp.Should().Be(timestamp);
        context.MessageName.Should().Be(messageName);
        context.Headers.Should().BeSameAs(headers);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void should_throw_when_message_id_is_null_empty_or_whitespace(string? messageId)
    {
        // when
        var act = () =>
            new ConsumeContext<TestMessage>
            {
                Lane = MessageLane.Bus,
                Message = new TestMessage("order-123", 99.99m),
                MessageId = messageId!,
                CorrelationId = null,
                Timestamp = DateTimeOffset.UtcNow,
                MessageName = "test.messageName",
                Headers = new MessageHeader(new Dictionary<string, string?>(StringComparer.Ordinal)),
            };

        // then
        act.Should().Throw<ArgumentException>().WithMessage("*MessageId cannot be null or whitespace*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void should_throw_when_correlation_id_is_empty_or_whitespace(string correlationId)
    {
        // when
        var act = () =>
            new ConsumeContext<TestMessage>
            {
                Lane = MessageLane.Bus,
                Message = new TestMessage("order-123", 99.99m),
                MessageId = Faker.Random.Guid().ToString(),
                CorrelationId = correlationId,
                Timestamp = DateTimeOffset.UtcNow,
                MessageName = "test.messageName",
                Headers = new MessageHeader(new Dictionary<string, string?>(StringComparer.Ordinal)),
            };

        // then
        act.Should().Throw<ArgumentException>().WithMessage("*CorrelationId cannot be an empty string*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void should_throw_when_tenant_id_is_empty_or_whitespace(string tenantId)
    {
        // when
        var act = () =>
            new ConsumeContext<TestMessage>
            {
                Lane = MessageLane.Bus,
                Message = new TestMessage("order-123", 99.99m),
                MessageId = Faker.Random.Guid().ToString(),
                CorrelationId = null,
                Timestamp = DateTimeOffset.UtcNow,
                MessageName = "test.messageName",
                Headers = new MessageHeader(new Dictionary<string, string?>(StringComparer.Ordinal)),
                TenantId = tenantId,
            };

        // then
        act.Should().Throw<ArgumentException>().WithMessage("*TenantId cannot be an empty or whitespace string*");
    }

    [Fact]
    public void should_allow_null_correlation_id()
    {
        // given
        var message = new TestMessage("order-123", 99.99m);

        // when
        var context = new ConsumeContext<TestMessage>
        {
            Lane = MessageLane.Bus,
            Message = message,
            MessageId = Faker.Random.Guid().ToString(),
            CorrelationId = null,
            Timestamp = DateTimeOffset.UtcNow,
            MessageName = "test.messageName",
            Headers = new MessageHeader(new Dictionary<string, string?>(StringComparer.Ordinal)),
        };

        // then
        context.CorrelationId.Should().BeNull();
    }

    [Fact]
    public void should_allow_null_tenant_id()
    {
        // given
        var message = new TestMessage("order-123", 99.99m);

        // when
        var context = new ConsumeContext<TestMessage>
        {
            Lane = MessageLane.Bus,
            Message = message,
            MessageId = Faker.Random.Guid().ToString(),
            CorrelationId = null,
            Timestamp = DateTimeOffset.UtcNow,
            MessageName = "test.messageName",
            Headers = new MessageHeader(new Dictionary<string, string?>(StringComparer.Ordinal)),
            TenantId = null,
        };

        // then
        context.TenantId.Should().BeNull();
    }

    [Fact]
    public void should_set_and_overwrite_response_header()
    {
        // given
        var context = _CreateContext(new TestMessage("1", 10m));

        // when
        context.SetResponseHeader("key1", "val1");
        context.SetResponseHeader("key2", "val2");
        context.SetResponseHeader("key1", "val1-overwritten");

        // then
        context.ResponseHeaders.Should().NotBeNull();
        context.ResponseHeaders!["key1"].Should().Be("val1-overwritten");
        context.ResponseHeaders["key2"].Should().Be("val2");
    }

    [Fact]
    public void should_allow_null_response_header_value()
    {
        // given
        var context = _CreateContext(new TestMessage("1", 10m));

        // when
        context.SetResponseHeader("nullable-key", null);

        // then
        context.ResponseHeaders.Should().NotBeNull();
        context.ResponseHeaders!["nullable-key"].Should().BeNull();
    }

    [Fact]
    public void should_set_response_destination()
    {
        // given
        var context = _CreateContext(new TestMessage("1", 10m));

        // when
        context.SetResponseDestination("dest.callback");

        // then
        context.ResponseDestination.Should().Be("dest.callback");
        context.IsResponseSuppressed.Should().BeFalse();
    }

    [Fact]
    public void should_suppress_response()
    {
        // given
        var context = _CreateContext(new TestMessage("1", 10m));

        // when
        context.SuppressResponse();

        // then
        context.IsResponseSuppressed.Should().BeTrue();
    }

    [Fact]
    public void should_unsuppress_when_setting_response_destination()
    {
        // given
        var context = _CreateContext(new TestMessage("1", 10m));
        context.SuppressResponse();
        context.IsResponseSuppressed.Should().BeTrue();

        // when
        context.SetResponseDestination("new.destination");

        // then
        context.IsResponseSuppressed.Should().BeFalse();
        context.ResponseDestination.Should().Be("new.destination");
    }

    [Fact]
    public void should_throw_when_mutators_called_after_completed()
    {
        // given
        var context = _CreateContext(new TestMessage("1", 10m));
        context.MarkCompleted();

        // when / then
        var actHeader = () => context.SetResponseHeader("key", "val");
        actHeader.Should().Throw<InvalidOperationException>().WithMessage("*read-only*");

        var actDest = () => context.SetResponseDestination("dest");
        actDest.Should().Throw<InvalidOperationException>().WithMessage("*read-only*");

        var actSuppress = () => context.SuppressResponse();
        actSuppress.Should().Throw<InvalidOperationException>().WithMessage("*read-only*");
    }

    private ConsumeContext<TestMessage> _CreateContext(TestMessage message)
    {
        return new ConsumeContext<TestMessage>
        {
            Lane = MessageLane.Bus,
            Message = message,
            MessageId = Faker.Random.Guid().ToString(),
            CorrelationId = null,
            Timestamp = DateTimeOffset.UtcNow,
            MessageName = "test.messageName",
            Headers = new MessageHeader(new Dictionary<string, string?>(StringComparer.Ordinal)),
        };
    }
}

public sealed record TestMessage(string OrderId, decimal Amount);
