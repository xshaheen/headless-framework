// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Messaging.Redis;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using MessagingHeaders = Headless.Messaging.Headers;

namespace Tests;

public sealed class RedisConsumerClientRawPayloadTests : TestBase
{
    private const string _RawName = "runs.result";
    private const string _StrictName = "orders.placed";
    private const long _StoredAtMs = 1_791_547_200_000;
    private const string _EntryId = "1791547200000-3";

    private static readonly string _RawStream = RedisPhysicalAddress.QueueStream(_RawName);
    private static readonly string _StrictStream = RedisPhysicalAddress.QueueStream(_StrictName);

    private readonly IRedisStreamManager _streams = Substitute.For<IRedisStreamManager>();

    [Fact]
    public async Task should_name_the_arrival_message_only_when_its_stream_was_subscribed()
    {
        // given
        await using var client = _CreateClient(new RedisMessagingOptions(), MessageLane.Bus);

        // when
        await client.SubscribeAsync([_RawName], AbortToken);

        // then
        client.ArrivalName(RedisPhysicalAddress.BusStream(_RawName)).Should().Be(_RawName);
        client.ArrivalName(RedisPhysicalAddress.QueueStream(_RawName)).Should().BeNull();
        client.ArrivalName(RedisPhysicalAddress.BusStream("runs.other")).Should().BeNull();
    }

    [Fact]
    public void should_identify_an_entry_by_its_stream_and_id_with_no_producer_id()
    {
        // when
        var facts = RedisConsumerClient.NativeFacts(_RawStream, _EntryId);

        // then
        facts.ProducerMessageId.Should().BeNull();
        facts.PositionId.Should().Be($"redis:{_RawStream}:{_EntryId}");
        facts.StoredAt.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(_StoredAtMs));
    }

    [Fact]
    public void should_give_no_stored_time_when_the_entry_id_milliseconds_are_out_of_range()
    {
        // when
        var facts = RedisConsumerClient.NativeFacts(_RawStream, "18446744073709551615-0");

        // then
        facts.PositionId.Should().Be($"redis:{_RawStream}:18446744073709551615-0");
        facts.StoredAt.Should().BeNull();
    }

    [Fact]
    public async Task should_complete_a_raw_entry_that_has_only_a_body()
    {
        // given
        var entry = new StreamEntry(_EntryId, [new NameValueEntry("body", """{"ok":true}"""u8.ToArray())]);

        // when
        var (message, error) = await _ConsumeAsync(_RawStream, _RawName, entry);

        // then
        error.Should().BeNull();
        message.Should().NotBeNull();
        var expectedId = $"redis:{_RawStream}:{_EntryId}";
        message!.Value.Headers[MessagingHeaders.MessageId].Should().Be(expectedId);
        message!.Value.Headers[MessagingHeaders.MessageName].Should().Be(_RawName);
        message!.Value.Headers[MessagingHeaders.ContractVersion].Should().Be("2");
        message!.Value.Headers[MessagingHeaders.CorrelationId].Should().Be(expectedId);
        message!.Value.Headers[MessagingHeaders.TransportAddress].Should().Be(_RawStream);
        DateTimeOffset
            .Parse(message!.Value.Headers[MessagingHeaders.SentTime]!, CultureInfo.InvariantCulture)
            .Should()
            .Be(DateTimeOffset.FromUnixTimeMilliseconds(_StoredAtMs));
        message!.Value.Body.ToArray().Should().Equal("""{"ok":true}"""u8.ToArray());
    }

    [Fact]
    public async Task should_keep_the_headers_a_raw_entry_carries()
    {
        // given
        var entry = new StreamEntry(
            _EntryId,
            [
                new NameValueEntry("headers", $$"""{"{{MessagingHeaders.MessageId}}":"producer-1"}"""),
                new NameValueEntry("body", "{}"u8.ToArray()),
            ]
        );

        // when
        var (message, error) = await _ConsumeAsync(_RawStream, _RawName, entry);

        // then
        error.Should().BeNull();
        message!.Value.Headers[MessagingHeaders.MessageId].Should().Be("producer-1");
        message!.Value.Headers[MessagingHeaders.MessageName].Should().Be(_RawName);
    }

    [Fact]
    public async Task should_reject_a_headerless_entry_when_its_route_is_not_raw()
    {
        // given
        var entry = new StreamEntry(_EntryId, [new NameValueEntry("body", "{}"u8.ToArray())]);

        // when
        var (message, error) = await _ConsumeAsync(_StrictStream, _StrictName, entry);

        // then
        message.Should().BeNull();
        error!.Exception.Should().BeOfType<RedisConsumeMissingHeadersException>();
        await _streams.Received(1).Ack(_StrictStream, "group", _EntryId, CancellationToken.None);
    }

    [Fact]
    public async Task should_reject_a_raw_entry_whose_header_names_another_contract()
    {
        // given
        var entry = new StreamEntry(
            _EntryId,
            [
                new NameValueEntry("headers", $$"""{"{{MessagingHeaders.MessageName}}":"{{_StrictName}}"}"""),
                new NameValueEntry("body", "{}"u8.ToArray()),
            ]
        );

        // when
        var (message, error) = await _ConsumeAsync(_RawStream, _RawName, entry);

        // then
        message.Should().BeNull();
        error!.Exception.Should().BeOfType<RedisConsumeInvalidHeadersException>();
        error.Exception.InnerException.Should().BeOfType<InvalidDataException>();
        error
            .Exception.InnerException!.Message.Should()
            .Be($"The Redis delivery names message '{_StrictName}' but arrived on the address of '{_RawName}'.");
        await _streams.Received(1).Ack(_RawStream, "group", _EntryId, CancellationToken.None);
    }

    [Fact]
    public async Task should_report_the_missing_header_reason_when_a_strict_entry_has_no_message_id()
    {
        // given
        var entry = new StreamEntry(
            _EntryId,
            [
                new NameValueEntry("headers", $$"""{"{{MessagingHeaders.MessageName}}":"{{_StrictName}}"}"""),
                new NameValueEntry("body", "{}"u8.ToArray()),
            ]
        );

        // when
        var (message, error) = await _ConsumeAsync(_StrictStream, _StrictName, entry);

        // then
        message.Should().BeNull();
        error!.Exception.Should().BeOfType<RedisConsumeInvalidHeadersException>();
        error
            .Exception.InnerException!.Message.Should()
            .Be("The Redis transport envelope is missing a required Messaging header.");
    }

    [Fact]
    public async Task should_report_the_over_long_id_reason_when_a_message_id_exceeds_the_limit()
    {
        // given
        var longId = new string('a', MessageOptions.MessageIdMaxLength + 1);
        var entry = new StreamEntry(
            _EntryId,
            [
                new NameValueEntry("headers", $$"""{"{{MessagingHeaders.MessageId}}":"{{longId}}"}"""),
                new NameValueEntry("body", "{}"u8.ToArray()),
            ]
        );

        // when
        var (message, error) = await _ConsumeAsync(_RawStream, _RawName, entry);

        // then
        message.Should().BeNull();
        error!.Exception.Should().BeOfType<RedisConsumeInvalidHeadersException>();
        var reason = error.Exception.InnerException!.Message;
        reason.Should().Contain($"longer than {MessageOptions.MessageIdMaxLength} characters");
        reason.Should().NotContain(longId);
    }

    [Fact]
    public async Task should_report_a_sanitized_and_bounded_reason_when_the_header_name_carries_control_characters()
    {
        // given
        // A JSON escape, so the parsed name carries a real line feed.
        var hostileName = "evil\\u000Aforged-log-line" + new string('x', 2_000);
        var entry = new StreamEntry(
            _EntryId,
            [
                new NameValueEntry("headers", $$"""{"{{MessagingHeaders.MessageName}}":"{{hostileName}}"}"""),
                new NameValueEntry("body", "{}"u8.ToArray()),
            ]
        );

        // when
        var (message, error) = await _ConsumeAsync(_RawStream, _RawName, entry);

        // then
        message.Should().BeNull();
        var reason = error!.Exception.InnerException!.Message;
        reason.Should().StartWith("The Redis delivery names message 'evilforged-log-line");
        reason.Should().NotContain("\n");
        reason.Length.Should().BeLessThanOrEqualTo(512 + 3);
    }

    [Fact]
    public async Task should_reject_a_raw_entry_whose_headers_field_is_not_a_json_object()
    {
        // given
        var entry = new StreamEntry(
            _EntryId,
            [new NameValueEntry("headers", "[1,2]"), new NameValueEntry("body", "{}"u8.ToArray())]
        );

        // when
        var (message, error) = await _ConsumeAsync(_RawStream, _RawName, entry);

        // then
        message.Should().BeNull();
        error!.Exception.Should().BeOfType<RedisConsumeInvalidHeadersException>();
        error.Exception.InnerException.Should().BeOfType<InvalidDataException>();
        error.Exception.InnerException!.Message.Should().Be("The Redis message headers could not be parsed.");
    }

    [Fact]
    public async Task should_reject_a_raw_entry_without_a_body_field()
    {
        // given
        var entry = new StreamEntry(_EntryId, [new NameValueEntry("payload", "{}")]);

        // when
        var (message, error) = await _ConsumeAsync(_RawStream, _RawName, entry);

        // then
        message.Should().BeNull();
        error!.Exception.Should().BeOfType<RedisConsumeMissingBodyException>();
    }

    [Fact]
    public async Task should_let_the_custom_headers_builder_overwrite_wire_headers_but_not_the_address()
    {
        // given
        var entry = new StreamEntry(
            _EntryId,
            [
                new NameValueEntry("headers", $$"""{"{{MessagingHeaders.MessageId}}":"wire-id"}"""),
                new NameValueEntry("body", "{}"u8.ToArray()),
            ]
        );
        var options = new RedisMessagingOptions
        {
            CustomHeadersBuilder = (streamEntry, _) =>
                [
                    new(MessagingHeaders.MessageId, $"built-{streamEntry.Id}"),
                    new(MessagingHeaders.TransportAddress, "elsewhere"),
                ],
        };

        // when
        var (message, error) = await _ConsumeAsync(_RawStream, _RawName, entry, options);

        // then
        error.Should().BeNull();
        message!.Value.Headers[MessagingHeaders.MessageId].Should().Be($"built-{_EntryId}");
        message!.Value.Headers[MessagingHeaders.TransportAddress].Should().Be(_RawStream);
    }

    [Fact]
    public async Task should_leave_the_entry_pending_when_the_custom_headers_builder_throws()
    {
        // given: a builder whose lookup is briefly down must not cost the entry
        var entry = new StreamEntry(_EntryId, [new NameValueEntry("body", "{}"u8.ToArray())]);
        var options = new RedisMessagingOptions
        {
            CustomHeadersBuilder = (_, _) => throw new TimeoutException("lookup of secret-header timed out"),
        };

        // when
        var (message, error) = await _ConsumeAsync(_RawStream, _RawName, entry, options);

        // then: the claim pass hands the unacknowledged entry out again
        message.Should().BeNull();
        error!.Exception.Message.Should().Contain(_EntryId).And.Contain(nameof(TimeoutException));
        error.Exception.Message.Should().NotContain("secret-header");
        error.Exception.InnerException.Should().BeNull();
        await _streams
            .DidNotReceive()
            .Ack(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private RedisConsumerClient _CreateClient(RedisMessagingOptions options, MessageLane lane = MessageLane.Queue)
    {
        var services = Substitute.For<IServiceProvider>();
        services
            .GetService(typeof(TransportEnvelopeGate))
            .Returns(new TransportEnvelopeGate([_Route(_RawName, "2", raw: true), _Route(_StrictName, "1")]));

        return new RedisConsumerClient(
            "group",
            0,
            _streams,
            Options.Create(options),
            LoggerFactory.CreateLogger<RedisConsumerClient>(),
            lane,
            serviceProvider: services
        );
    }

    private async Task<(TransportMessage? Message, RedisMessagingOptions.ConsumeErrorContext? Error)> _ConsumeAsync(
        string stream,
        string messageName,
        StreamEntry entry,
        RedisMessagingOptions? options = null
    )
    {
        options ??= new RedisMessagingOptions();
        var outcome = new TaskCompletionSource<(TransportMessage?, RedisMessagingOptions.ConsumeErrorContext?)>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        options.OnConsumeError = context =>
        {
            outcome.TrySetResult((null, context));
            return Task.CompletedTask;
        };
        _SetupGroupReads(new RedisStreamMessages(stream, [entry]));

        await using var client = _CreateClient(options);
        client.AttachCallbacks(
            (message, _) =>
            {
                outcome.TrySetResult((message, null));
                return Task.CompletedTask;
            },
            onLog: null
        );
        await client.SubscribeAsync([messageName], AbortToken);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);

        var listening = client.ListeningAsync(TimeSpan.FromMilliseconds(10), cts.Token);
        var result = await outcome.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        await cts.CancelAsync();
        await listening;

        return result;
    }

    private void _SetupGroupReads(RedisStreamMessages pending)
    {
        _streams
            .PollStreamsPendingMessagesAsync(
                Arg.Any<string[]>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<Action<Exception?>?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_Messages(pending));
        _streams
            .PollStreamsStalePendingMessagesAsync(
                Arg.Any<string[]>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<Action<Exception?>?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_Messages());
        _streams
            .PollStreamsLatestMessagesAsync(
                Arg.Any<string[]>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<Action<Exception?>?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_Messages());
    }

    private static async IAsyncEnumerable<IEnumerable<RedisStreamMessages>> _Messages(
        RedisStreamMessages? stream = null
    )
    {
        if (stream is { } value)
        {
            yield return [value];
        }

        await Task.CompletedTask;
    }

    private static MessageMetadata _Route(string name, string version, bool raw = false) =>
        new(
            new MessageRouteKey(typeof(object), name, MessageLane.Queue),
            typeof(object),
            version,
            CorrelationSelector: null,
            new Dictionary<Type, object>(),
            RawPayload: raw
        );
}
