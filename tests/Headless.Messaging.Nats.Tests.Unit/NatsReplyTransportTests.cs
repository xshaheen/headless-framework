// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Nats;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MessagingHeaders = Headless.Messaging.Headers;

namespace Tests;

public sealed class NatsReplyTransportTests : TestBase
{
    private readonly INatsConnectionPool _pool = Substitute.For<INatsConnectionPool>();

    [Fact]
    public async Task should_declare_request_reply_and_register_the_nats_reply_transport()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(setup => setup.UseNats("nats://localhost:4222"));

        // when
        await using var provider = services.BuildServiceProvider();

        // then
        provider
            .GetServices<MessagingProviderCapabilities>()
            .Single(x => x.Role == MessagingProviderRole.Transport)
            .SupportsRequestReply.Should()
            .BeTrue();
        provider.GetRequiredService<IReplyTransport>().Should().BeOfType<NatsReplyTransport>();
    }

    [Fact]
    public void should_accept_a_created_reply_address_and_one_with_more_tokens()
    {
        // given
        var transport = _CreateTransport();

        // when
        var created = transport.IsReplyAddress(ReplyAddresses.Create());
        var tokens = transport.IsReplyAddress("headless.reply.host-1.call_2");

        // then
        created.Should().BeTrue();
        tokens.Should().BeTrue();
    }

    [Theory]
    [InlineData("headless.queue.orders.place")]
    [InlineData("headless.bus.billing")]
    [InlineData("_INBOX.abc")]
    [InlineData("headless.reply.")]
    [InlineData("headless.reply.*")]
    [InlineData("headless.reply.>")]
    [InlineData("headless.reply.a b")]
    [InlineData("headless.reply..forged")]
    [InlineData("headless.reply.forged.")]
    [InlineData("headless.reply.a..b")]
    public async Task should_refuse_an_address_outside_the_reply_namespace_before_using_a_connection(string address)
    {
        // given
        var transport = _CreateTransport();

        // when
        var act = () => transport.SendAsync(address, _Reply(), AbortToken).AsTask();

        // then
        transport.IsReplyAddress(address).Should().BeFalse();
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName(nameof(address));
        _pool.DidNotReceive().GetConnection();
    }

    private NatsReplyTransport _CreateTransport()
    {
        return new NatsReplyTransport(_pool, TimeProvider.System, NullLogger<NatsReplyTransport>.Instance);
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
