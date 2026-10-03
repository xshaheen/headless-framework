// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Redis;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MessagingHeaders = Headless.Messaging.Headers;

namespace Tests;

public sealed class RedisReplyTransportTests : TestBase
{
    private readonly IRedisConnectionPool _pool = Substitute.For<IRedisConnectionPool>();

    [Fact]
    public async Task should_declare_request_reply_and_register_the_redis_reply_transport()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(setup => setup.UseRedis("localhost:6379"));

        // when
        await using var provider = services.BuildServiceProvider();

        // then
        provider
            .GetServices<MessagingProviderCapabilities>()
            .Single(x => x.Role == MessagingProviderRole.Transport)
            .SupportsRequestReply.Should()
            .BeTrue();
        provider.GetRequiredService<IReplyTransport>().Should().BeOfType<RedisReplyTransport>();
    }

    [Fact]
    public void should_accept_a_created_reply_address()
    {
        // given
        IReplyTransport transport = _CreateTransport();

        // when
        var accepted = transport.IsReplyAddress(ReplyAddresses.Create());

        // then
        accepted.Should().BeTrue();
    }

    [Theory]
    [InlineData("headless:messaging:queue:orders.place")]
    [InlineData("headless:messaging:bus:billing")]
    [InlineData("orders")]
    [InlineData("headless.reply.")]
    [InlineData("headless.reply.*")]
    [InlineData("headless.reply.a b")]
    [InlineData("__keyspace@0__:headless.reply.x")]
    public async Task should_refuse_an_address_outside_the_reply_namespace_before_using_a_connection(string address)
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
        await _pool.DidNotReceive().ConnectAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void should_round_trip_reply_headers_and_body_through_the_pub_sub_payload()
    {
        // given
        var reply = _Reply();

        // when
        var decoded = RedisMessage.CreateReply(reply.AsReplyPayload());

        // then
        decoded.Headers.Should().BeEquivalentTo(reply.Headers);
        decoded.Body.ToArray().Should().Equal(reply.Body.ToArray());
    }

    [Fact]
    public void should_round_trip_an_empty_reply_body()
    {
        // given
        var reply = new TransportMessage(
            new Dictionary<string, string?>(StringComparer.Ordinal) { [MessagingHeaders.MessageId] = "reply-1" },
            ReadOnlyMemory<byte>.Empty
        );

        // when
        var decoded = RedisMessage.CreateReply(reply.AsReplyPayload());

        // then
        decoded.Body.IsEmpty.Should().BeTrue();
        decoded.Headers[MessagingHeaders.MessageId].Should().Be("reply-1");
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("{\"body\":\"e30=\"}")]
    public void should_reject_a_payload_that_is_not_a_reply(string payload)
    {
        // when
        var act = () => RedisMessage.CreateReply(Encoding.UTF8.GetBytes(payload));

        // then
        act.Should().Throw<JsonException>();
    }

    private RedisReplyTransport _CreateTransport()
    {
        return new RedisReplyTransport(_pool, TimeProvider.System, NullLogger<RedisReplyTransport>.Instance);
    }

    private static TransportMessage _Reply()
    {
        return new TransportMessage(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [MessagingHeaders.MessageId] = "reply-1",
                [MessagingHeaders.InReplyTo] = "request-1",
                [MessagingHeaders.ReplyStatus] = null,
            },
            "{\"price\":42}"u8.ToArray()
        );
    }
}
