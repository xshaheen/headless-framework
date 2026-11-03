// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Testing.Tests;

namespace Tests.Internal;

public sealed class ConsumeMessageBindingTests : TestBase
{
    private static readonly DateTimeOffset _Added = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    [Fact]
    public void should_bind_every_envelope_header_into_the_typed_context()
    {
        // given
        var binding = ConsumeMessageBinding.For<BindingPayload>();
        var payload = new BindingPayload("value");
        var medium = _Medium(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.MessageId] = "msg-1",
                [Headers.MessageName] = "orders.placed",
                [Headers.CorrelationId] = "corr-1",
                [Headers.CausationId] = "cause-1",
                [Headers.ContractVersion] = "v3",
                [Headers.TransportAddress] = "queue://orders",
                [Headers.SentTime] = "2026-05-06T07:08:09+02:00",
            }
        );
        var headers = new MessageHeader(medium.Origin.Headers);

        // when
        var context = binding.CreateContext(payload, medium, headers, "tenant-1", MessageLane.Queue);

        // then
        var typed = context.Should().BeOfType<ConsumeContext<BindingPayload>>().Subject;
        typed.Message.Should().BeSameAs(payload);
        typed.MessageType.Should().Be<BindingPayload>();
        typed.MessageId.Should().Be("msg-1");
        typed.MessageName.Should().Be("orders.placed");
        typed.CorrelationId.Should().Be("corr-1");
        typed.CausationId.Should().Be("cause-1");
        typed.ContractVersion.Should().Be("v3");
        typed.TransportAddress.Should().Be("queue://orders");
        typed.TenantId.Should().Be("tenant-1");
        typed.Lane.Should().Be(MessageLane.Queue);
        typed.Headers.Should().BeSameAs(headers);
        typed.Timestamp.Should().Be(new DateTimeOffset(2026, 5, 6, 5, 8, 9, TimeSpan.Zero));
    }

    [Fact]
    public void should_default_optional_headers_when_they_are_absent_or_blank()
    {
        // given
        var binding = ConsumeMessageBinding.For<BindingPayload>();
        var medium = _Medium(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.MessageId] = "msg-1",
                [Headers.MessageName] = "orders.placed",
                [Headers.CorrelationId] = "  ",
                [Headers.CausationId] = "",
                [Headers.TransportAddress] = " ",
                [Headers.SentTime] = "not-a-time",
            }
        );

        // when
        var context = binding.CreateContext(
            new BindingPayload("value"),
            medium,
            new MessageHeader(medium.Origin.Headers),
            tenantId: null,
            MessageLane.Bus
        );

        // then
        context.CorrelationId.Should().BeNull();
        context.CausationId.Should().BeNull();
        context.TransportAddress.Should().BeNull();
        context.ContractVersion.Should().Be(MessageOptions.InitialContractVersion);
        context.TenantId.Should().BeNull();
        context.Timestamp.Should().Be(_Added);
    }

    [Fact]
    public void should_refuse_an_envelope_without_a_message_id()
    {
        // given
        var binding = ConsumeMessageBinding.For<BindingPayload>();
        var medium = _Medium(
            new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageName] = "orders.placed" }
        );

        // when
        var act = () =>
            binding.CreateContext(
                new BindingPayload("value"),
                medium,
                new MessageHeader(medium.Origin.Headers),
                tenantId: null,
                MessageLane.Bus
            );

        // then
        act.Should().ThrowExactly<KeyNotFoundException>();
    }

    [Fact]
    public void should_resolve_the_same_binding_from_a_type_argument_and_its_handle()
    {
        // when
        var fromHandle = ConsumeMessageBinding.From(MessageTypeHandle.For<BindingPayload>());

        // then
        fromHandle.Should().BeSameAs(ConsumeMessageBinding.For<BindingPayload>());
        fromHandle.MessageType.Should().Be<BindingPayload>();
        fromHandle.MiddlewareServiceType.Should().Be<IConsumeMiddleware<ConsumeContext<BindingPayload>>>();
    }

    [Fact]
    public async Task should_invoke_typed_middleware_with_the_typed_context()
    {
        // given
        var binding = ConsumeMessageBinding.For<BindingPayload>();
        var medium = _Medium(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.MessageId] = "msg-1",
                [Headers.MessageName] = "orders.placed",
            }
        );
        var context = binding.CreateContext(
            new BindingPayload("value"),
            medium,
            new MessageHeader(medium.Origin.Headers),
            tenantId: null,
            MessageLane.Bus
        );
        var middleware = new RecordingTypedMiddleware();
        var nextCalled = false;

        // when
        await binding.InvokeMiddlewareAsync(
            middleware,
            context,
            () =>
            {
                nextCalled = true;
                return ValueTask.CompletedTask;
            }
        );

        // then
        middleware.Context.Should().BeSameAs(context);
        nextCalled.Should().BeTrue();
    }

    private static MediumMessage _Medium(Dictionary<string, string?> headers)
    {
        return new MediumMessage
        {
            StorageId = Guid.NewGuid(),
            Origin = new Message(headers, value: null),
            Content = "{}",
            Lane = MessageLane.Bus,
            Added = _Added,
        };
    }

    private sealed class RecordingTypedMiddleware : IConsumeMiddleware<ConsumeContext<BindingPayload>>
    {
        public ConsumeContext<BindingPayload>? Context { get; private set; }

        public ValueTask InvokeAsync(ConsumeContext<BindingPayload> context, Func<ValueTask> next)
        {
            Context = context;
            return next();
        }
    }
}

public sealed record BindingPayload(string Value);
