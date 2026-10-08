// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Nats;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NSubstitute.Core;
using INatsConnectionPool = Headless.Messaging.Nats.INatsConnectionPool;
using MessagingHeaders = Headless.Messaging.Headers;
using MsOptions = Microsoft.Extensions.Options;

namespace Tests;

public sealed class NatsTransportTests : TestBase
{
    private readonly ILogger<NatsTransport> _logger;
    private readonly INatsConnectionPool _pool;

    private readonly NatsStreamProvisioner _provisioner = new(
        MsOptions.Options.Create(new NatsMessagingOptions { StreamProvisioning = NatsStreamProvisioning.Disabled })
    );

    public NatsTransportTests()
    {
        _logger = NullLogger<NatsTransport>.Instance;
        _pool = Substitute.For<INatsConnectionPool>();
        _pool.ServersAddress.Returns("nats://localhost:4222");
    }

    [Fact]
    public async Task should_return_failed_result_without_sending_when_sending_after_dispose()
    {
        var transport = new NatsTransport(_logger, _pool, _provisioner);
        await transport.DisposeAsync();

        var result = await transport.SendAsync(_CreateTransportMessage("msg-123", "TestMessage"), AbortToken);

        result.Succeeded.Should().BeFalse();
        result.Exception.Should().BeOfType<ObjectDisposedException>();
        _pool.DidNotReceive().GetConnection();
    }

    [Fact]
    public async Task should_have_correct_broker_address()
    {
        await using var transport = new NatsTransport(_logger, _pool, _provisioner);

        transport.BrokerAddress.Name.Should().Be("nats");
        transport.BrokerAddress.Endpoint.Should().Be("nats://localhost:4222");
    }

    [Fact]
    public async Task should_propagate_cancellation()
    {
        await using var transport = new NatsTransport(_logger, _pool, _provisioner);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await transport.SendAsync(_CreateTransportMessage("msg-123", "TestMessage"), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_provision_the_stream_again_on_the_next_publish_when_no_stream_answers_a_publish()
    {
        // given - the stream was provisioned, then deleted while the host runs, so no stream answers the publish
        var js = _CreateJetStreamWithoutStreams();
        var provisioner = new NatsStreamProvisioner(
            MsOptions.Options.Create(new NatsMessagingOptions { StreamProvisioning = NatsStreamProvisioning.Verify })
        );
        await provisioner.EnsureForPublishAsync(js, MessageLane.Bus, "orders.placed", false, AbortToken);
        var connection = _CreateConnection(_ => throw new NatsNoReplyException());
        _pool.GetConnection().Returns(connection);
        await using var transport = new NatsTransport(_logger, _pool, provisioner);

        // when
        var result = await transport.SendAsync(_CreateTransportMessage("msg-123", "orders.placed"), AbortToken);
        await provisioner.EnsureForPublishAsync(js, MessageLane.Bus, "orders.placed", false, AbortToken);

        // then - the failed publish dropped the remembered ensure, so the next publish creates the stream again
        result.Succeeded.Should().BeFalse();
        result
            .Exception.Should()
            .BeOfType<PublisherSentFailedException>()
            .Which.InnerException.Should()
            .BeOfType<NatsJSPublishNoResponseException>();
        result.Exception!.Message.Should().Contain("stream 'headless-bus-orders'");
        await js.Received(2).CreateStreamAsync(Arg.Any<StreamConfig>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_return_failed_result_when_a_cancellation_the_caller_did_not_request_ends_the_publish()
    {
        // given - an internal timeout, not the caller's token, cancels the publish
        var connection = _CreateConnection(_ => throw new OperationCanceledException());
        _pool.GetConnection().Returns(connection);
        await using var transport = new NatsTransport(_logger, _pool, _provisioner);

        // when
        var result = await transport.SendAsync(_CreateTransportMessage("msg-123", "orders.placed"), AbortToken);

        // then - the outbox retries a failed send; only the caller's own cancellation propagates
        result.Succeeded.Should().BeFalse();
        result
            .Exception.Should()
            .BeOfType<PublisherSentFailedException>()
            .Which.InnerException.Should()
            .BeAssignableTo<OperationCanceledException>();
    }

    [Fact]
    public async Task should_dispose_without_error()
    {
        await using var transport = new NatsTransport(_logger, _pool, _provisioner);

        // ReSharper disable once DisposeOnUsingVariable
        var act = async () => await transport.DisposeAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void should_use_message_id_for_jetstream_deduplication_when_create_publish_opts()
    {
        var opts = NatsTransport.CreatePublishOpts(_CreateTransportMessage("msg-123", "TestMessage"));

        opts.Should().BeEquivalentTo(new NatsJSPubOpts { MsgId = "msg-123" });
    }

    [Fact]
    public void should_append_valid_subject_shard_when_resolve_subject()
    {
        var message = _CreateTransportMessage("msg-123", "orders.created");
        message.Headers[NatsMessagingHeaders.SubjectShard] = "tenant-a";

        NatsTransport.ResolveSubject(message).Should().Be("headless.bus.orders.created.tenant-a");
    }

    [Fact]
    public void should_ignore_invalid_subject_shard_and_fall_back_to_message_name_when_resolve_subject()
    {
        var message = _CreateTransportMessage("msg-123", "orders.created");
        message.Headers[NatsMessagingHeaders.SubjectShard] = "tenant.a";

        NatsTransport.ResolveSubject(message).Should().Be("headless.bus.orders.created");
    }

    [Fact]
    public void should_return_null_when_create_publish_headers_all_values_are_null()
    {
        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal) { { "key1", null }, { "key2", null } },
            body: "test"u8.ToArray()
        );

        NatsTransport.CreatePublishHeaders(message).Should().BeNull();
    }

    [Fact]
    public void should_include_only_non_null_values_when_create_publish_headers()
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
    public void should_return_null_when_create_publish_headers_headers_are_empty()
    {
        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal),
            body: "test"u8.ToArray()
        );

        NatsTransport.CreatePublishHeaders(message).Should().BeNull();
    }

    private static INatsConnection _CreateConnection(Func<CallInfo, ValueTask<NatsMsg<PubAckResponse>>> publish)
    {
        var connection = Substitute.For<INatsConnection>();
        // Direct reply mode sends a JetStream publish through RequestAsync, so the substitute decides its outcome.
        connection.Opts.Returns(NatsOpts.Default with { RequestReplyMode = NatsRequestReplyMode.Direct });
        connection
            .RequestAsync<ReadOnlyMemory<byte>, PubAckResponse>(
                Arg.Any<string>(),
                Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<NatsHeaders?>(),
                Arg.Any<INatsSerialize<ReadOnlyMemory<byte>>?>(),
                Arg.Any<INatsDeserialize<PubAckResponse>?>(),
                Arg.Any<NatsPubOpts?>(),
                Arg.Any<NatsSubOpts?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(publish);
        return connection;
    }

    private static INatsJSContext _CreateJetStreamWithoutStreams()
    {
        var js = Substitute.For<INatsJSContext>();
        js.GetStreamAsync(Arg.Any<string>(), Arg.Any<StreamInfoRequest?>(), Arg.Any<CancellationToken>())
            .Returns<ValueTask<INatsJSStream>>(_ =>
                throw new NatsJSApiException(new ApiError { Code = 404, ErrCode = 10059 })
            );
        js.CreateStreamAsync(Arg.Any<StreamConfig>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var stream = Substitute.For<INatsJSStream>();
                stream.Info.Returns(new StreamInfo { Config = call.Arg<StreamConfig>() });
                return new ValueTask<INatsJSStream>(stream);
            });
        return js;
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
