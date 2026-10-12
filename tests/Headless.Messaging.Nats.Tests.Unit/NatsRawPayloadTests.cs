// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Messaging.Nats;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using NATS.Client.Core;
using NATS.Client.JetStream;
using MsOptions = Microsoft.Extensions.Options;

namespace Tests;

public sealed class NatsRawPayloadTests : TestBase
{
    private const string _RawName = "runs.result";
    private const string _StrictName = "orders.placed";
    private const string _RawSubject = "headless.bus.runs.result";

    // 2026-10-09T12:00:00.123Z plus 456789 ns, which NATS.Net's metadata truncates to the millisecond.
    private const long _StoredNanos = 1_791_633_600_123_456_789;

    private readonly MsOptions.IOptions<NatsMessagingOptions> _options = MsOptions.Options.Create(
        new NatsMessagingOptions { Servers = "nats://localhost:4222" }
    );

    [Fact]
    public void should_resolve_a_subscribed_name_from_its_exact_subject()
    {
        // given
        var arrivals = NatsArrivalNames.Create(MessageLane.Bus, [_RawName, _StrictName], _NoShards(), []);

        // when
        var name = arrivals.Resolve(_RawSubject);

        // then
        name.Should().Be(_RawName);
    }

    [Fact]
    public void should_resolve_a_shard_subject_to_its_sharded_name()
    {
        // given
        var arrivals = NatsArrivalNames.Create(
            MessageLane.Queue,
            ["orders"],
            new HashSet<string>(StringComparer.Ordinal) { "orders" },
            []
        );

        // when
        var name = arrivals.Resolve("headless.queue.orders.tenant-a");

        // then
        name.Should().Be("orders");
    }

    [Theory]
    [InlineData("headless.bus.orders.tenant-a", "the name is not sharded")]
    [InlineData("headless.bus.unknown", "no subscription names it")]
    [InlineData("headless.queue.orders", "the subject is on another lane")]
    public void should_not_resolve_a_subject_no_subscription_names(string subject, string because)
    {
        // given
        var arrivals = NatsArrivalNames.Create(MessageLane.Bus, ["orders"], _NoShards(), []);

        // when
        var name = arrivals.Resolve(subject);

        // then
        name.Should().BeNull(because);
    }

    [Fact]
    public void should_not_claim_another_message_subject_or_a_multi_token_tail_for_a_sharded_name()
    {
        // given: 'orders' is sharded, so its 'orders.>' filter also matches the dotted name 'orders.audit'
        var arrivals = NatsArrivalNames.Create(
            MessageLane.Bus,
            ["orders"],
            new HashSet<string>(StringComparer.Ordinal) { "orders" },
            ["orders", "orders.audit"]
        );

        // when
        var otherMessage = arrivals.Resolve("headless.bus.orders.audit");
        var multiToken = arrivals.Resolve("headless.bus.orders.a.b");

        // then
        otherMessage.Should().BeNull("the subject is another message's own address");
        multiToken.Should().BeNull("a shard is a single token");
    }

    [Fact]
    public void should_take_the_producer_id_from_nats_msg_id()
    {
        // given
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal) { ["Nats-Msg-Id"] = "producer-7" };

        // when
        var facts = NatsConsumerClient.NativeFacts(headers, _JetStreamMsg(_V1ReplyTo(42)));

        // then
        facts.ProducerMessageId.Should().Be("producer-7");
    }

    [Fact]
    public void should_derive_the_position_from_stream_sequence_and_full_stored_nanos()
    {
        // given
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal);

        // when
        var v1 = NatsConsumerClient.NativeFacts(headers, _JetStreamMsg(_V1ReplyTo(42)));
        var v2 = NatsConsumerClient.NativeFacts(headers, _JetStreamMsg(_V2ReplyTo(42)));

        // then
        v1.ProducerMessageId.Should().BeNull();
        v1.PositionId.Should().Be($"nats:HEADLESS-BUS-RUNS:42:{_StoredNanos}");
        v2.PositionId.Should().Be(v1.PositionId);
        v1.StoredAt.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(_StoredNanos / 1_000_000));
    }

    [Fact]
    public void should_give_no_position_to_a_jetstream_delivery_without_metadata()
    {
        // given
        var msg = Substitute.For<INatsJSMsg<ReadOnlyMemory<byte>>>();
        msg.Metadata.Returns((NatsJSMsgMetadata?)null);

        // when
        var facts = NatsConsumerClient.NativeFacts(new Dictionary<string, string?>(StringComparer.Ordinal), msg);

        // then
        facts.PositionId.Should().BeNull();
        facts.StoredAt.Should().BeNull();
    }

    [Fact]
    public void should_give_a_core_delivery_a_fresh_version_7_id()
    {
        // given
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal);

        // when
        var first = NatsConsumerClient.NativeFacts(headers, jetStream: null);
        var second = NatsConsumerClient.NativeFacts(headers, jetStream: null);

        // then
        Guid.Parse(first.PositionId!).Version.Should().Be(7);
        first.PositionId.Should().NotBe(second.PositionId);
    }

    [Fact]
    public async Task should_complete_a_headerless_jetstream_delivery_on_a_raw_route()
    {
        // given
        var msg = _JetStreamMsg(_V1ReplyTo(42), subject: _RawSubject);
        await using var services = _Services();
        await using var client = await _SubscribedClientAsync(services);
        TransportMessage? received = null;
        client.OnMessageCallback = (message, _) =>
        {
            received = message;
            return Task.CompletedTask;
        };

        // when
        await client.DispatchEnvelopeAsync(_RawSubject, natsHeaders: null, "{}"u8.ToArray(), msg, msg, AbortToken);

        // then
        received.Should().NotBeNull();
        received!.Value.Headers[Headers.MessageName].Should().Be(_RawName);
        received!.Value.Headers[Headers.MessageId].Should().Be($"nats:HEADLESS-BUS-RUNS:42:{_StoredNanos}");
        received!.Value.Headers[Headers.ContractVersion].Should().Be("2");
        received!.Value.Headers[Headers.TransportAddress].Should().Be(_RawSubject);
    }

    [Fact]
    public async Task should_prefer_nats_msg_id_over_the_position_for_a_raw_delivery()
    {
        // given
        var msg = _JetStreamMsg(_V1ReplyTo(42), subject: _RawSubject);
        await using var services = _Services();
        await using var client = await _SubscribedClientAsync(services);
        TransportMessage? received = null;
        client.OnMessageCallback = (message, _) =>
        {
            received = message;
            return Task.CompletedTask;
        };
        var headers = new NatsHeaders { ["Nats-Msg-Id"] = "producer-7" };

        // when
        await client.DispatchEnvelopeAsync(_RawSubject, headers, "{}"u8.ToArray(), msg, msg, AbortToken);

        // then
        received!.Value.Headers[Headers.MessageId].Should().Be("producer-7");
    }

    [Fact]
    public async Task should_complete_a_headerless_core_delivery_on_a_raw_route()
    {
        // given
        await using var services = _Services();
        await using var client = await _SubscribedClientAsync(services);
        TransportMessage? received = null;
        client.OnMessageCallback = (message, _) =>
        {
            received = message;
            return Task.CompletedTask;
        };

        // when
        await client.DispatchEnvelopeAsync(
            _RawSubject,
            natsHeaders: null,
            "{}"u8.ToArray(),
            jsMsg: null,
            settlement: new object(),
            AbortToken
        );

        // then
        received.Should().NotBeNull();
        Guid.Parse(received!.Value.Headers[Headers.MessageId]!).Version.Should().Be(7);
    }

    [Fact]
    public async Task should_terminate_a_headerless_delivery_on_a_strict_route()
    {
        // given
        const string subject = "headless.bus.orders.placed";
        var msg = _JetStreamMsg(_V1ReplyTo(42), subject: subject);
        await using var services = _Services();
        await using var client = await _SubscribedClientAsync(services);
        var callbackInvoked = false;
        client.OnMessageCallback = (_, _) =>
        {
            callbackInvoked = true;
            return Task.CompletedTask;
        };

        // when
        await client.DispatchEnvelopeAsync(subject, natsHeaders: null, "{}"u8.ToArray(), msg, msg, AbortToken);

        // then
        callbackInvoked.Should().BeFalse();
        await msg.Received(1).AckTerminateAsync(Arg.Any<AckOpts?>(), CancellationToken.None);
    }

    [Fact]
    public async Task should_terminate_a_raw_delivery_whose_header_names_another_contract()
    {
        // given
        var msg = _JetStreamMsg(_V1ReplyTo(42), subject: _RawSubject);
        await using var services = _Services();
        await using var client = await _SubscribedClientAsync(services);
        var callbackInvoked = false;
        client.OnMessageCallback = (_, _) =>
        {
            callbackInvoked = true;
            return Task.CompletedTask;
        };
        var headers = new NatsHeaders { [Headers.MessageName] = _StrictName, [Headers.MessageId] = "m-1" };

        // when
        await client.DispatchEnvelopeAsync(_RawSubject, headers, "{}"u8.ToArray(), msg, msg, AbortToken);

        // then
        callbackInvoked.Should().BeFalse();
        await msg.Received(1).AckTerminateAsync(Arg.Any<AckOpts?>(), CancellationToken.None);
    }

    [Fact]
    public async Task should_terminate_a_raw_jetstream_delivery_with_no_identity()
    {
        // given: no Nats-Msg-Id, and a reply subject NATS.Net could not parse into metadata
        var msg = Substitute.For<INatsJSMsg<ReadOnlyMemory<byte>>>();
        msg.Subject.Returns(_RawSubject);
        msg.Metadata.Returns((NatsJSMsgMetadata?)null);
        await using var services = _Services();
        await using var client = await _SubscribedClientAsync(services);
        var callbackInvoked = false;
        client.OnMessageCallback = (_, _) =>
        {
            callbackInvoked = true;
            return Task.CompletedTask;
        };

        // when
        await client.DispatchEnvelopeAsync(_RawSubject, natsHeaders: null, "{}"u8.ToArray(), msg, msg, AbortToken);

        // then
        callbackInvoked.Should().BeFalse();
        await msg.Received(1).AckTerminateAsync(Arg.Any<AckOpts?>(), CancellationToken.None);
    }

    private async Task<NatsConsumerClient> _SubscribedClientAsync(IServiceProvider services)
    {
        var client = new NatsConsumerClient("test-group", 0, _options, services) { OnLogCallback = _ => { } };
        await client.SubscribeAsync([_RawName, _StrictName], AbortToken);
        return client;
    }

    private static ServiceProvider _Services()
    {
        MessageMetadata[] routes = [_Route(_RawName, "2", raw: true), _Route(_StrictName, "1", raw: false)];
        var registry = Substitute.For<IMessageMetadataRegistry>();
        registry.GetAll().Returns(routes);

        return new ServiceCollection()
            .AddSingleton(registry)
            .AddSingleton(new TransportEnvelopeGate(routes))
            .BuildServiceProvider();
    }

    private static MessageMetadata _Route(string name, string version, bool raw) =>
        new(
            new MessageRouteKey(typeof(object), name, MessageLane.Bus),
            typeof(object),
            version,
            CorrelationSelector: null,
            new Dictionary<Type, object>(),
            RawPayload: raw
        );

    private static HashSet<string> _NoShards() => new(StringComparer.Ordinal);

    private static INatsJSMsg<ReadOnlyMemory<byte>> _JetStreamMsg(string replyTo, string subject = _RawSubject)
    {
        var msg = Substitute.For<INatsJSMsg<ReadOnlyMemory<byte>>>();
        msg.Subject.Returns(subject);
        msg.ReplyTo.Returns(replyTo);
        msg.Metadata.Returns(
            new NatsJSMsgMetadata(
                new NatsJSSequencePair(42, 7),
                NumDelivered: 1,
                NumPending: 0,
                DateTimeOffset.FromUnixTimeMilliseconds(_StoredNanos / 1_000_000),
                "HEADLESS-BUS-RUNS",
                "consumer",
                string.Empty
            )
        );
        return msg;
    }

    private static string _V1ReplyTo(ulong streamSequence) =>
        $"$JS.ACK.HEADLESS-BUS-RUNS.consumer.1.{streamSequence}.7.{_StoredNanos}.0";

    private static string _V2ReplyTo(ulong streamSequence) =>
        $"$JS.ACK._.ACCHASH.HEADLESS-BUS-RUNS.consumer.1.{streamSequence}.7.{_StoredNanos}.0.rand";
}
