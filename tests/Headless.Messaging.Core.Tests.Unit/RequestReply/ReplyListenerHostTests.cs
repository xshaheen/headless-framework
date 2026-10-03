// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.RequestReply;
using Headless.Messaging.Serialization;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Tests.RequestReply;

/// <summary>
/// The reply listener host turns the listener's lifecycle into what a caller sees: a listener that never opens fails
/// the call as not sent, and a stop that overlaps the open closes the late listener instead of leaking it.
/// </summary>
public sealed class ReplyListenerHostTests : TestBase
{
    private readonly IReplyTransport _transport = Substitute.For<IReplyTransport>();
    private readonly PendingRequests _pending = new();
    private readonly ReplyListenerHost _host;

    public ReplyListenerHostTests()
    {
        _host = new ReplyListenerHost(
            _transport,
            new ReplyDispatcher(
                _pending,
                new JsonUtf8Serializer(Options.Create(new MessagingOptions())),
                NullLogger<ReplyDispatcher>.Instance
            ),
            _pending,
            new FakeTimeProvider(),
            NullLogger<ReplyListenerHost>.Instance
        );
    }

    [Fact]
    public async Task should_fail_bootstrap_and_every_waiting_call_when_the_listener_cannot_open()
    {
        // given — the transport cannot open its listener
        var cause = new InvalidOperationException("broker down");
        _transport
            .OpenListenerAsync(
                Arg.Any<Func<TransportMessage, CancellationToken, ValueTask>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ => ValueTask.FromException<IReplyListener>(cause));

        // when
        var start = () => _host.StartAsync(AbortToken).AsTask();
        var wait = () => _host.WaitForAddressAsync(AbortToken).AsTask();

        // then — bootstrap sees the cause itself, and a call sees it as the reason the request was not sent
        (await start.Should().ThrowAsync<InvalidOperationException>())
            .Which.Should()
            .BeSameAs(cause);
        var notSent = await wait.Should().ThrowAsync<RequestNotSentException>();
        notSent.Which.InnerException.Should().BeSameAs(cause);
        notSent.Which.Message.Should().NotContain("stopping");
    }

    [Fact]
    public async Task should_close_a_listener_that_opens_after_the_host_began_to_stop()
    {
        // given — the open is still in flight when shutdown begins
        var opening = new TaskCompletionSource<IReplyListener>(TaskCreationOptions.RunContinuationsAsynchronously);
        var listener = Substitute.For<IReplyListener>();
        _transport
            .OpenListenerAsync(
                Arg.Any<Func<TransportMessage, CancellationToken, ValueTask>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ => new ValueTask<IReplyListener>(opening.Task));
        var start = _host.StartAsync(AbortToken).AsTask();

        // when
        _host.Quiesce();
        opening.SetResult(listener);
        await start.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);

        // then — the late listener is closed rather than kept, and a call learns the requester is stopping
        await listener.Received(1).DisposeAsync();
        _pending.IsClosed.Should().BeTrue();
        var wait = () => _host.WaitForAddressAsync(AbortToken).AsTask();
        await wait.Should().ThrowAsync<RequestNotSentException>().WithMessage("*requester is stopping*");
    }

    [Fact]
    public async Task should_stop_without_error_when_the_listener_never_opened()
    {
        // when — stopped before, or without, a start
        var stop = () => _host.StopAsync(TimeSpan.FromSeconds(1)).AsTask();

        // then — nothing to close, nothing to fail, and the requester refuses new calls
        await stop.Should().NotThrowAsync();
        await _transport
            .DidNotReceive()
            .OpenListenerAsync(
                Arg.Any<Func<TransportMessage, CancellationToken, ValueTask>>(),
                Arg.Any<CancellationToken>()
            );
        var wait = () => _host.WaitForAddressAsync(AbortToken).AsTask();
        await wait.Should().ThrowAsync<RequestNotSentException>().WithMessage("*requester is stopping*");
    }
}
