// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Redis;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute.ExceptionExtensions;
using StackExchange.Redis;

namespace Tests;

public sealed class RedisBusTransportTests : TestBase
{
    [Fact]
    public async Task should_publish_to_lane_qualified_bus_stream()
    {
        var streamManager = Substitute.For<IRedisStreamManager>();
        var options = Options.Create(
            new RedisMessagingOptions { Configuration = ConfigurationOptions.Parse("localhost:6379") }
        );
        await using var transport = new RedisBusTransport(
            streamManager,
            options,
            LoggerFactory.CreateLogger<RedisBusTransport>()
        );
        var message = new TransportMessage(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.MessageId] = "message-1",
                [Headers.MessageName] = "orders.created",
            },
            "body"u8.ToArray()
        );

        var result = await transport.SendAsync(message, AbortToken);

        result.Succeeded.Should().BeTrue();
        await streamManager
            .Received(1)
            .PublishAsync("headless:messaging:bus:orders.created", Arg.Any<NameValueEntry[]>(), AbortToken);
    }

    [Fact]
    public async Task should_propagate_cancellation_before_publish()
    {
        var streamManager = Substitute.For<IRedisStreamManager>();
        await using var transport = _CreateTransport(streamManager);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => transport.SendAsync(_CreateMessage(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        await streamManager
            .DidNotReceive()
            .PublishAsync(Arg.Any<string>(), Arg.Any<NameValueEntry[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_return_failed_result_when_stream_publish_fails()
    {
        var streamManager = Substitute.For<IRedisStreamManager>();
        var failure = new RedisException("connection failed");
        streamManager
            .PublishAsync(Arg.Any<string>(), Arg.Any<NameValueEntry[]>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(failure);
        await using var transport = _CreateTransport(streamManager);

        var result = await transport.SendAsync(_CreateMessage(), AbortToken);

        result.Succeeded.Should().BeFalse();
        result.Exception!.InnerException.Should().BeSameAs(failure);
    }

    private static RedisBusTransport _CreateTransport(IRedisStreamManager streamManager)
    {
        var options = Options.Create(
            new RedisMessagingOptions { Configuration = ConfigurationOptions.Parse("localhost:6379") }
        );
        return new RedisBusTransport(streamManager, options, NullLogger<RedisBusTransport>.Instance);
    }

    private static TransportMessage _CreateMessage()
    {
        return new TransportMessage(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.MessageId] = "message-1",
                [Headers.MessageName] = "orders.created",
            },
            "body"u8.ToArray()
        );
    }
}
