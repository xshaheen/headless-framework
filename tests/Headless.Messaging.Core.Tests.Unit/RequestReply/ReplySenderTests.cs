// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests.RequestReply;

/// <summary>
/// Covers the one place replies leave a responder host: a request names its own reply address, so an address outside
/// the reserved reply namespace must never reach the transport.
/// </summary>
/// <remarks>
/// Its dropped-reply listener is process-wide, so it shares the request/reply collection with the caller tests, whose
/// late replies would otherwise land in its measurements.
/// </remarks>
[Collection(RequestReplyCollection.Name)]
public sealed class ReplySenderTests : TestBase
{
    private const string _ValidAddress = "headless.reply.0f8fad5bd9cb469fa16570867728950e";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("orders.placed")]
    [InlineData("headless.reply.")]
    [InlineData("headless.reply.abc>")]
    [InlineData("headless.reply.abc*")]
    [InlineData("headless.reply.abc def")]
    [InlineData("HEADLESS.REPLY.abc")]
    public async Task should_refuse_an_address_outside_the_reply_namespace_before_any_write_and_count_it(
        string? address
    )
    {
        // given: a lenient transport, so the refusal comes from the messaging core itself
        var transport = Substitute.For<IReplyTransport>();
        transport.IsReplyAddress(Arg.Any<string>()).Returns(true);
        var sender = new ReplySender(transport, NullLogger<ReplySender>.Instance);
        using var measurements = new RequestReplyMeasurements();

        // when
        var sent = await sender.SendAsync(address, _Reply(), AbortToken);

        // then
        sent.Should().BeFalse();
        await transport.DidNotReceiveWithAnyArgs().SendAsync(default!, default, AbortToken);
        measurements.InvalidAddressDrops.Should().Equal("invalid_reply_address");
    }

    [Fact]
    public async Task should_refuse_an_address_the_transport_does_not_accept()
    {
        // given
        var transport = Substitute.For<IReplyTransport>();
        transport.IsReplyAddress(_ValidAddress).Returns(false);
        var sender = new ReplySender(transport, NullLogger<ReplySender>.Instance);
        using var measurements = new RequestReplyMeasurements();

        // when
        var sent = await sender.SendAsync(_ValidAddress, _Reply(), AbortToken);

        // then
        sent.Should().BeFalse();
        await transport.DidNotReceiveWithAnyArgs().SendAsync(default!, default, AbortToken);
        measurements.InvalidAddressDrops.Should().Equal("invalid_reply_address");
    }

    [Fact]
    public async Task should_send_a_reply_to_an_address_inside_the_reply_namespace()
    {
        // given
        var transport = Substitute.For<IReplyTransport>();
        transport.IsReplyAddress(_ValidAddress).Returns(true);
        var sender = new ReplySender(transport, NullLogger<ReplySender>.Instance);
        var reply = _Reply();

        // when
        var sent = await sender.SendAsync(_ValidAddress, reply, AbortToken);

        // then
        sent.Should().BeTrue();
        await transport.Received(1).SendAsync(_ValidAddress, reply, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_absorb_a_transport_failure_because_a_reply_is_never_retried()
    {
        // given
        var transport = Substitute.For<IReplyTransport>();
        transport.IsReplyAddress(_ValidAddress).Returns(true);
        transport
            .SendAsync(default!, default, AbortToken)
            .ReturnsForAnyArgs(_ => ValueTask.FromException(new InvalidOperationException("broker unreachable")));
        var sender = new ReplySender(transport, NullLogger<ReplySender>.Instance);

        // when
        var sent = await sender.SendAsync(_ValidAddress, _Reply(), AbortToken);

        // then
        sent.Should().BeFalse();
    }

    private static TransportMessage _Reply()
    {
        return new TransportMessage(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.MessageId] = "reply-1",
                [Headers.InReplyTo] = "request-1",
                [Headers.ReplyStatus] = "ok",
            },
            "{}"u8.ToArray()
        );
    }
}
