// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.InMemory;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests;

public sealed class InMemoryReplyTransportTests : TestBase
{
    [Fact]
    public async Task should_deliver_a_reply_sent_to_a_listener_address_to_its_handler()
    {
        // given
        var transport = _CreateTransport(_CreateQueue());
        var received = new TaskCompletionSource<TransportMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var listener = await transport.OpenListenerAsync(
            (reply, _) =>
            {
                received.TrySetResult(reply);
                return ValueTask.CompletedTask;
            },
            AbortToken
        );
        var address = await listener.WaitForAddressAsync(AbortToken);

        // when
        await transport.SendAsync(address, _Reply("request-1"), AbortToken);

        // then
        var reply = await received.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        reply.Headers[Headers.InReplyTo].Should().Be("request-1");
        reply.Body.ToArray().Should().Equal("{}"u8.ToArray());
    }

    [Fact]
    public async Task should_give_each_listener_its_own_address_inside_the_reply_namespace()
    {
        // given
        var transport = _CreateTransport(_CreateQueue());
        await using var first = await transport.OpenListenerAsync(static (_, _) => ValueTask.CompletedTask, AbortToken);
        await using var second = await transport.OpenListenerAsync(
            static (_, _) => ValueTask.CompletedTask,
            AbortToken
        );

        // when
        var firstAddress = await first.WaitForAddressAsync(AbortToken);
        var secondAddress = await second.WaitForAddressAsync(AbortToken);

        // then
        firstAddress.Should().StartWith("headless.reply.");
        secondAddress.Should().StartWith("headless.reply.");
        firstAddress.Should().NotBe(secondAddress);
        transport.IsReplyAddress(firstAddress).Should().BeTrue();
    }

    [Fact]
    public async Task should_drop_without_error_a_reply_to_an_unknown_address_inside_the_reply_namespace()
    {
        // given
        var transport = _CreateTransport(_CreateQueue());

        // when
        var act = async () =>
            await transport.SendAsync("headless.reply.0f8fad5bd9cb469fa16570867728950e", _Reply("r"), AbortToken);

        // then
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task should_stop_delivery_once_the_listener_is_closed()
    {
        // given
        var transport = _CreateTransport(_CreateQueue());
        var deliveries = 0;
        var listener = await transport.OpenListenerAsync(
            (_, _) =>
            {
                Interlocked.Increment(ref deliveries);
                return ValueTask.CompletedTask;
            },
            AbortToken
        );
        var address = await listener.WaitForAddressAsync(AbortToken);

        // when
        await listener.DisposeAsync();
        await transport.SendAsync(address, _Reply("request-1"), AbortToken);

        // then: the send completes synchronously into the closed map, so there is nothing to wait for
        Volatile.Read(ref deliveries).Should().Be(0);
        var waitAfterClose = async () => await listener.WaitForAddressAsync(AbortToken);
        await waitAfterClose.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Theory]
    [InlineData("orders.placed")]
    [InlineData("headless.reply.")]
    [InlineData("headless.reply.abc>")]
    public async Task should_refuse_an_address_outside_the_reply_namespace(string address)
    {
        // given
        var transport = _CreateTransport(_CreateQueue());

        // when
        var act = async () => await transport.SendAsync(address, _Reply("request-1"), AbortToken);

        // then
        transport.IsReplyAddress(address).Should().BeFalse();
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task should_deliver_replies_across_hosts_that_share_one_memory_queue()
    {
        // given
        var shared = _CreateQueue();
        var callerHost = _CreateTransport(shared);
        var responderHost = _CreateTransport(shared);
        var received = new TaskCompletionSource<TransportMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var listener = await callerHost.OpenListenerAsync(
            (reply, _) =>
            {
                received.TrySetResult(reply);
                return ValueTask.CompletedTask;
            },
            AbortToken
        );
        var address = await listener.WaitForAddressAsync(AbortToken);

        // when
        await responderHost.SendAsync(address, _Reply("request-7"), AbortToken);

        // then
        var reply = await received.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        reply.Headers[Headers.InReplyTo].Should().Be("request-7");
    }

    [Fact]
    public async Task should_keep_delivering_after_a_handler_throws()
    {
        // given
        var transport = _CreateTransport(_CreateQueue());
        var second = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var listener = await transport.OpenListenerAsync(
            (reply, _) =>
            {
                var requestId = reply.Headers[Headers.InReplyTo];
                if (string.Equals(requestId, "first", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("handler failed");
                }

                second.TrySetResult(requestId);
                return ValueTask.CompletedTask;
            },
            AbortToken
        );
        var address = await listener.WaitForAddressAsync(AbortToken);

        // when
        await transport.SendAsync(address, _Reply("first"), AbortToken);
        await transport.SendAsync(address, _Reply("second"), AbortToken);

        // then
        (await second.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken))
            .Should()
            .Be("second");
    }

    private static MemoryQueue _CreateQueue() => new(NullLogger<MemoryQueue>.Instance);

    private static IReplyTransport _CreateTransport(MemoryQueue queue) =>
        new InMemoryReplyTransport(queue, NullLogger<InMemoryReplyTransport>.Instance);

    private static TransportMessage _Reply(string requestId)
    {
        return new TransportMessage(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.MessageId] = Guid.NewGuid().ToString("N"),
                [Headers.MessageName] = "pricing.quote",
                [Headers.InReplyTo] = requestId,
                [Headers.ReplyStatus] = "ok",
            },
            "{}"u8.ToArray()
        );
    }
}
