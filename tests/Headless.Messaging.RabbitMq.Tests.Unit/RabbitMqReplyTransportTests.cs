// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.RabbitMq;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;
using MessagingHeaders = Headless.Messaging.Headers;

namespace Tests;

public sealed class RabbitMqReplyTransportTests : TestBase
{
    private readonly IConnectionChannelPool _pool = Substitute.For<IConnectionChannelPool>();
    private readonly IChannel _channel = Substitute.For<IChannel>();

    public RabbitMqReplyTransportTests()
    {
        _pool.Rent(Arg.Any<CancellationToken>()).Returns(_channel);
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        await _channel.DisposeAsync();
        await base.DisposeAsyncCore();
    }

    [Fact]
    public async Task should_declare_request_reply_and_register_the_rabbitmq_reply_transport()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(setup =>
            setup.UseRabbitMq(options =>
            {
                options.HostName = "localhost";
                options.UserName = "reply-tests";
                options.Password = "reply-tests-secret";
            })
        );

        // when
        await using var provider = services.BuildServiceProvider();

        // then
        provider
            .GetServices<MessagingProviderCapabilities>()
            .Single(x => x.Role == MessagingProviderRole.Transport)
            .SupportsRequestReply.Should()
            .BeTrue();
        provider.GetRequiredService<IReplyTransport>().Should().BeOfType<RabbitMqReplyTransport>();
    }

    [Fact]
    public async Task should_publish_a_reply_through_the_default_exchange_to_the_reply_queue()
    {
        // given
        var transport = _CreateTransport();
        var address = ReplyAddresses.Create();

        // when
        await transport.SendAsync(address, _Reply(), AbortToken);

        // then
        await _channel
            .Received(1)
            .BasicPublishAsync(
                string.Empty,
                address,
                false,
                Arg.Is<BasicProperties>(p => p.MessageId == "reply-1" && p.DeliveryMode == DeliveryModes.Transient),
                Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<CancellationToken>()
            );
        _pool.Received(1).Return(_channel);
    }

    [Theory]
    [InlineData("amq.gen-JzTY20BRgKO-HjmUJj0wLg")]
    [InlineData("queue.orders.place")]
    [InlineData("bus.billing")]
    [InlineData("headless.reply.")]
    [InlineData("headless.reply.#")]
    [InlineData("headless.reply.a b")]
    public async Task should_refuse_an_address_outside_the_reply_namespace_before_renting_a_channel(string address)
    {
        // given
        var transport = _CreateTransport();

        // when
        var act = () => transport.SendAsync(address, _Reply(), AbortToken).AsTask();

        // then
        ((IReplyTransport)transport)
            .IsReplyAddress(address)
            .Should()
            .BeFalse();
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName(nameof(address));
        _pool
            .ReceivedCalls()
            .Should()
            .NotContain(call => call.GetMethodInfo().Name == nameof(IConnectionChannelPool.Rent));
    }

    private RabbitMqReplyTransport _CreateTransport()
    {
        return new RabbitMqReplyTransport(_pool, NullLogger<RabbitMqReplyTransport>.Instance);
    }

    private static TransportMessage _Reply()
    {
        return new TransportMessage(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [MessagingHeaders.MessageId] = "reply-1",
                [MessagingHeaders.InReplyTo] = "request-1",
            },
            "{}"u8.ToArray()
        );
    }
}
