// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;
using Headless.Messaging.Nats;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NATS.Client.JetStream;
using MessagingHeaders = Headless.Messaging.Headers;
using NatsHeaders = NATS.Client.Core.NatsHeaders;

namespace Tests;

public sealed class NatsTransportTests : TestBase
{
    private readonly ILogger<NatsTransport> _logger;
    private readonly INatsConnectionPool _pool;

    public NatsTransportTests()
    {
        _logger = NullLogger<NatsTransport>.Instance;
        _pool = Substitute.For<INatsConnectionPool>();
        _pool.ServersAddress.Returns("nats://localhost:4222");
    }

    [Fact]
    public async Task should_have_correct_broker_address()
    {
        await using var transport = new NatsTransport(_logger, _pool);

        transport.BrokerAddress.Name.Should().Be("nats");
        transport.BrokerAddress.Endpoint.Should().Be("nats://localhost:4222");
    }

    [Fact]
    public async Task should_propagate_cancellation()
    {
        await using var transport = new NatsTransport(_logger, _pool);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await transport.SendAsync(_CreateTransportMessage("msg-123", "TestMessage"), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_dispose_without_error()
    {
        await using var transport = new NatsTransport(_logger, _pool);

        // ReSharper disable once DisposeOnUsingVariable
        // ReSharper disable once AccessToDisposedClosure
        var act = async () => await transport.DisposeAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void CreatePublishOpts_should_use_message_id_for_jetstream_deduplication()
    {
        var opts = NatsTransport.CreatePublishOpts(_CreateTransportMessage("msg-123", "TestMessage"));

        opts.Should().BeEquivalentTo(new NatsJSPubOpts { MsgId = "msg-123" });
    }

    [Fact]
    public void CreatePublishHeaders_should_return_null_when_all_values_are_null()
    {
        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal) { { "key1", null }, { "key2", null } },
            body: "test"u8.ToArray()
        );

        NatsTransport.CreatePublishHeaders(message).Should().BeNull();
    }

    [Fact]
    public void CreatePublishHeaders_should_include_only_non_null_values()
    {
        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                { "key1", "value1" },
                { "key2", null },
                { "key3", "value3" },
            },
            body: "test"u8.ToArray()
        );

        var headers = NatsTransport.CreatePublishHeaders(message);

        headers.Should().NotBeNull();
        headers!.Should().HaveCount(2);
        headers["key1"].ToString().Should().Be("value1");
        headers["key3"].ToString().Should().Be("value3");
    }

    [Fact]
    public void CreatePublishHeaders_should_return_null_when_headers_are_empty()
    {
        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal),
            body: "test"u8.ToArray()
        );

        NatsTransport.CreatePublishHeaders(message).Should().BeNull();
    }

    private static TransportMessage _CreateTransportMessage(string messageId, string messageName)
    {
        return new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                { MessagingHeaders.MessageId, messageId },
                { MessagingHeaders.MessageName, messageName },
            },
            body: "test-body"u8.ToArray()
        );
    }
}
