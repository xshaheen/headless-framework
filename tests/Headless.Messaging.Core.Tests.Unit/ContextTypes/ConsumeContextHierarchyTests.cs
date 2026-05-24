// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Testing.Tests;

namespace Tests.ContextTypes;

public sealed class ConsumeContextHierarchyTests : TestBase
{
    [Fact]
    public void should_expose_typed_and_object_message_from_generic_context()
    {
        // given
        var message = new OrderPlaced("order-1");

        // when
        var context = _CreateContext(message);
        ConsumeContext baseContext = context;

        // then
        context.Message.Should().BeSameAs(message);
        baseContext.Message.Should().BeSameAs(message);
        baseContext.MessageType.Should().Be<OrderPlaced>();
    }

    [Fact]
    public void should_preserve_record_with_expression_behavior()
    {
        // given
        var original = _CreateContext(new OrderPlaced("order-1"));

        // when
        var changed = original with
        {
            MessageId = "message-2",
        };

        // then
        changed.Should().NotBeSameAs(original);
        changed.Message.Should().BeSameAs(original.Message);
        changed.MessageId.Should().Be("message-2");
        original.MessageId.Should().Be("message-1");
    }

    [Fact]
    public void should_update_consume_cancellation_token_for_subsequent_reads()
    {
        // given
        using var cts = new CancellationTokenSource();
        var context = _CreateContext(new OrderPlaced("order-1"));

        // when
        context.WithCancellationToken(cts.Token);

        // then
        context.CancellationToken.Should().Be(cts.Token);
    }

    [Fact]
    public void should_allow_subclassing_generic_consume_context()
    {
        // when
        var context = new DerivedConsumeContext
        {
            IntentType = IntentType.Bus,
            Message = new OrderPlaced("order-1"),
            MessageId = "message-1",
            CorrelationId = null,
            TenantId = null,
            Headers = new MessageHeader(new Dictionary<string, string?>(StringComparer.Ordinal)),
            Timestamp = DateTimeOffset.UtcNow,
            Topic = "orders",
        };

        // then
        context.Should().BeAssignableTo<ConsumeContext<OrderPlaced>>();
        context.MessageType.Should().Be<OrderPlaced>();
    }

    private static ConsumeContext<OrderPlaced> _CreateContext(OrderPlaced message)
    {
        return new ConsumeContext<OrderPlaced>
        {
            IntentType = IntentType.Bus,
            Message = message,
            MessageId = "message-1",
            CorrelationId = "correlation-1",
            TenantId = "tenant-1",
            Headers = new MessageHeader(new Dictionary<string, string?>(StringComparer.Ordinal)),
            Timestamp = DateTimeOffset.UtcNow,
            Topic = "orders",
        };
    }

    private sealed record OrderPlaced(string OrderId);

    private sealed record DerivedConsumeContext : ConsumeContext<OrderPlaced>;
}
