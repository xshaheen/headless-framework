// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Serialization;
using Headless.Messaging.Testing;
using Headless.Messaging.Testing.Internal;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using NSubstitute;

namespace Tests;

public sealed class RecordingInfrastructureTests : TestBase
{
    // ─── helpers ─────────────────────────────────────────────────────────────

    private static IDictionary<string, string?> _BaseHeaders(
        string id = "msg-1",
        string name = "test-topic",
        string? correlationId = null,
        string? typeName = null
    )
    {
        var h = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = id,
            [Headers.MessageName] = name,
        };

        if (correlationId != null)
        {
            h[Headers.CorrelationId] = correlationId;
        }

        if (typeName != null)
        {
            h[Headers.Type] = typeName;
        }

        return h;
    }

    private static MediumMessage _MakeMediumMessage(
        string id = "msg-1",
        string name = "test-topic",
        string? correlationId = null
    )
    {
        return new MediumMessage
        {
            StorageId = 1,
            Content = "{}",
            Added = DateTime.UtcNow,
            Origin = new Message(_BaseHeaders(id, name, correlationId), value: null),
        };
    }

    private static ConsumerContext _MakeConsumerContext(MediumMessage medium)
    {
        var descriptor = new ConsumerExecutorDescriptor
        {
            MethodInfo = typeof(RecordingInfrastructureTests).GetMethod(
                nameof(_MakeConsumerContext),
                BindingFlags.NonPublic | BindingFlags.Static,
                [typeof(MediumMessage)]
            )!,
            ImplTypeInfo = typeof(RecordingInfrastructureTests).GetTypeInfo(),
            TopicName = medium.Origin.Headers[Headers.MessageName]!,
            GroupName = "test-group",
        };

        return new ConsumerContext(descriptor, medium);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // RecordingTransport
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RecordingTransport_records_published_message_on_success()
    {
        // given
        var store = new MessageObservationStore();
        var inner = new FakeTransport(OperateResult.Success);
        var serializer = Substitute.For<ISerializer>();
        var transport = new RecordingTransport(inner, store, serializer);

        // when
        await transport.SendAsync(new TransportMessage(_BaseHeaders(), ReadOnlyMemory<byte>.Empty), AbortToken);

        // then
        store.Published.Should().ContainSingle();
        store.Consumed.Should().BeEmpty();
        store.Faulted.Should().BeEmpty();
    }

    [Fact]
    public async Task RecordingTransport_does_not_record_on_failure()
    {
        // given
        var store = new MessageObservationStore();
        var inner = new FakeTransport(OperateResult.Failed(new Exception("broker error")));
        var serializer = Substitute.For<ISerializer>();
        var transport = new RecordingTransport(inner, store, serializer);

        // when
        await transport.SendAsync(new TransportMessage(_BaseHeaders(), ReadOnlyMemory<byte>.Empty), AbortToken);

        // then
        store.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task RecordingTransport_extracts_message_id_and_topic_from_headers()
    {
        // given
        var store = new MessageObservationStore();
        var inner = new FakeTransport(OperateResult.Success);
        var serializer = Substitute.For<ISerializer>();
        var transport = new RecordingTransport(inner, store, serializer);

        // when
        await transport.SendAsync(
            new TransportMessage(_BaseHeaders(id: "id-42", name: "my-topic"), ReadOnlyMemory<byte>.Empty),
            AbortToken
        );

        // then
        var recorded = store.Published.Single();
        recorded.MessageId.Should().Be("id-42");
        recorded.Topic.Should().Be("my-topic");
    }

    [Fact]
    public async Task RecordingTransport_extracts_correlation_id_from_headers()
    {
        // given
        var store = new MessageObservationStore();
        var inner = new FakeTransport(OperateResult.Success);
        var serializer = Substitute.For<ISerializer>();
        var transport = new RecordingTransport(inner, store, serializer);

        // when
        await transport.SendAsync(
            new TransportMessage(_BaseHeaders(correlationId: "corr-99"), ReadOnlyMemory<byte>.Empty),
            AbortToken
        );

        // then
        store.Published.Single().CorrelationId.Should().Be("corr-99");
    }

    [Fact]
    public async Task RecordingTransport_sets_null_correlation_id_when_absent()
    {
        // given
        var store = new MessageObservationStore();
        var inner = new FakeTransport(OperateResult.Success);
        var serializer = Substitute.For<ISerializer>();
        var transport = new RecordingTransport(inner, store, serializer);

        // when
        await transport.SendAsync(new TransportMessage(_BaseHeaders(), ReadOnlyMemory<byte>.Empty), AbortToken);

        // then
        store.Published.Single().CorrelationId.Should().BeNull();
    }

    [Fact]
    public async Task RecordingTransport_deserializes_body_when_type_header_present()
    {
        // given
        var store = new MessageObservationStore();
        var inner = new FakeTransport(OperateResult.Success);
        var payload = new SimplePayload { Value = "hello" };
        var typeName = typeof(SimplePayload).AssemblyQualifiedName!;
        var headers = _BaseHeaders(typeName: typeName);
        var body = new ReadOnlyMemory<byte>(JsonSerializer.SerializeToUtf8Bytes(payload));
        var transportMsg = new TransportMessage(headers, body);
        var serializer = new FakeSerializer(new Message(headers, payload));
        var transport = new RecordingTransport(inner, store, serializer);

        // when
        await transport.SendAsync(transportMsg, AbortToken);

        // then
        var recorded = store.Published.Single();
        recorded.MessageType.Should().Be(typeof(SimplePayload));
        recorded.Message.Should().BeOfType<SimplePayload>().Which.Value.Should().Be("hello");
    }

    [Fact]
    public async Task RecordingTransport_falls_back_to_TransportMessage_when_type_unresolvable()
    {
        // given
        var store = new MessageObservationStore();
        var inner = new FakeTransport(OperateResult.Success);
        var serializer = Substitute.For<ISerializer>();
        var transport = new RecordingTransport(inner, store, serializer);

        var headers = _BaseHeaders(typeName: "Nonexistent.Type, NonexistentAssembly");

        // when
        await transport.SendAsync(new TransportMessage(headers, new byte[] { 1 }), AbortToken);

        // then
        var recorded = store.Published.Single();
        recorded.MessageType.Should().Be(typeof(TransportMessage));
        recorded.Message.Should().BeOfType<TransportMessage>();
    }

    [Fact]
    public async Task RecordingTransport_falls_back_gracefully_when_deserialization_throws()
    {
        // given
        var store = new MessageObservationStore();
        var inner = new FakeTransport(OperateResult.Success);
        var serializer = new FakeSerializer(new InvalidOperationException("serializer exploded"));
        var typeName = typeof(SimplePayload).AssemblyQualifiedName!;
        var headers = _BaseHeaders(typeName: typeName);
        var transport = new RecordingTransport(inner, store, serializer);

        // when — must not throw
        await transport.SendAsync(new TransportMessage(headers, new byte[] { 1 }), AbortToken);

        // then — still records, just with fallback type
        store.Published.Should().ContainSingle();
        store.Published.Single().MessageType.Should().Be(typeof(TransportMessage));
    }

    [Fact]
    public async Task RecordingTransport_forwards_to_inner_transport()
    {
        // given
        var store = new MessageObservationStore();
        var inner = new FakeTransport(OperateResult.Success);
        var serializer = Substitute.For<ISerializer>();
        var transport = new RecordingTransport(inner, store, serializer);

        // when
        await transport.SendAsync(new TransportMessage(_BaseHeaders(), ReadOnlyMemory<byte>.Empty), AbortToken);

        // then
        inner.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task RecordingTransport_sets_timestamp_to_utc_now()
    {
        // given
        var store = new MessageObservationStore();
        var inner = new FakeTransport(OperateResult.Success);
        var serializer = Substitute.For<ISerializer>();
        var transport = new RecordingTransport(inner, store, serializer);

        var before = DateTimeOffset.UtcNow;

        // when
        await transport.SendAsync(new TransportMessage(_BaseHeaders(), ReadOnlyMemory<byte>.Empty), AbortToken);
        var after = DateTimeOffset.UtcNow;

        // then
        store.Published.Single().Timestamp.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // RecordingConsumeExecutionPipeline
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RecordingConsumeExecutionPipeline_records_consumed_on_success()
    {
        // given
        var store = new MessageObservationStore();
        var medium = _MakeMediumMessage();
        var context = _MakeConsumerContext(medium);
        var payload = new SimplePayload { Value = "ok" };
        var inner = new FakePipeline(new ConsumerExecutedResult(null, "msg-1", null, null));
        var pipeline = new RecordingConsumeExecutionPipeline(inner, store);

        // when
        await pipeline.ExecuteAsync(context, payload, typeof(SimplePayload), AbortToken);

        // then
        store.Consumed.Should().ContainSingle();
        store.Faulted.Should().BeEmpty();
        store.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task RecordingConsumeExecutionPipeline_records_faulted_and_rethrows_on_handler_failure()
    {
        // given
        var store = new MessageObservationStore();
        var medium = _MakeMediumMessage();
        var context = _MakeConsumerContext(medium);
        var payload = new SimplePayload();
        var ex = new InvalidOperationException("handler blew up");
        var inner = new FakePipeline(ex);
        var pipeline = new RecordingConsumeExecutionPipeline(inner, store);

        // when
        var act = async () => await pipeline.ExecuteAsync(context, payload, typeof(SimplePayload), AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("handler blew up");
        store.Faulted.Should().ContainSingle();
        store.Consumed.Should().BeEmpty();
    }

    [Fact]
    public async Task RecordingConsumeExecutionPipeline_faulted_entry_carries_exception()
    {
        // given
        var store = new MessageObservationStore();
        var medium = _MakeMediumMessage();
        var context = _MakeConsumerContext(medium);
        var payload = new SimplePayload();
        var ex = new InvalidOperationException("boom");
        var inner = new FakePipeline(ex);
        var pipeline = new RecordingConsumeExecutionPipeline(inner, store);

        // when
        try
        {
            await pipeline.ExecuteAsync(context, payload, typeof(SimplePayload), AbortToken);
        }
        catch
        { /* expected */
        }

        // then
        store.Faulted.Single().Exception.Should().BeSameAs(ex);
    }

    [Fact]
    public async Task RecordingConsumeExecutionPipeline_extracts_message_id_and_topic_from_context()
    {
        // given
        var store = new MessageObservationStore();
        var medium = _MakeMediumMessage(id: "ctx-id-7", name: "ctx-topic");
        var context = _MakeConsumerContext(medium);
        var payload = new SimplePayload();
        var inner = new FakePipeline(new ConsumerExecutedResult(null, "ctx-id-7", null, null));
        var pipeline = new RecordingConsumeExecutionPipeline(inner, store);

        // when
        await pipeline.ExecuteAsync(context, payload, typeof(SimplePayload), AbortToken);

        // then
        var recorded = store.Consumed.Single();
        recorded.MessageId.Should().Be("ctx-id-7");
        recorded.Topic.Should().Be("ctx-topic");
    }

    [Fact]
    public async Task RecordingConsumeExecutionPipeline_extracts_correlation_id_from_context()
    {
        // given
        var store = new MessageObservationStore();
        var medium = _MakeMediumMessage(correlationId: "corr-abc");
        var context = _MakeConsumerContext(medium);
        var payload = new SimplePayload();
        var inner = new FakePipeline(new ConsumerExecutedResult(null, "msg-1", null, null));
        var pipeline = new RecordingConsumeExecutionPipeline(inner, store);

        // when
        await pipeline.ExecuteAsync(context, payload, typeof(SimplePayload), AbortToken);

        // then
        store.Consumed.Single().CorrelationId.Should().Be("corr-abc");
    }

    [Fact]
    public async Task RecordingConsumeExecutionPipeline_sets_correct_message_type()
    {
        // given
        var store = new MessageObservationStore();
        var medium = _MakeMediumMessage();
        var context = _MakeConsumerContext(medium);
        var payload = new SimplePayload();
        var inner = new FakePipeline(new ConsumerExecutedResult(null, "msg-1", null, null));
        var pipeline = new RecordingConsumeExecutionPipeline(inner, store);

        // when
        await pipeline.ExecuteAsync(context, payload, typeof(SimplePayload), AbortToken);

        // then
        store.Consumed.Single().MessageType.Should().Be(typeof(SimplePayload));
    }

    [Fact]
    public async Task RecordingConsumeExecutionPipeline_forwards_to_inner_pipeline()
    {
        // given
        var store = new MessageObservationStore();
        var medium = _MakeMediumMessage();
        var context = _MakeConsumerContext(medium);
        var payload = new SimplePayload();
        var inner = new FakePipeline(new ConsumerExecutedResult(null, "msg-1", null, null));
        var pipeline = new RecordingConsumeExecutionPipeline(inner, store);

        // when
        await pipeline.ExecuteAsync(context, payload, typeof(SimplePayload), AbortToken);

        // then
        inner.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task RecordingConsumeExecutionPipeline_sets_null_correlation_id_when_absent()
    {
        // given
        var store = new MessageObservationStore();
        var medium = _MakeMediumMessage(); // no correlationId
        var context = _MakeConsumerContext(medium);
        var payload = new SimplePayload();
        var inner = new FakePipeline(new ConsumerExecutedResult(null, "msg-1", null, null));
        var pipeline = new RecordingConsumeExecutionPipeline(inner, store);

        // when
        await pipeline.ExecuteAsync(context, payload, typeof(SimplePayload), AbortToken);

        // then
        store.Consumed.Single().CorrelationId.Should().BeNull();
    }

    // ─── fakes ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Stub <see cref="ISerializer"/> that either returns a fixed <see cref="Message"/> or throws a fixed exception
    /// from <see cref="DeserializeAsync"/>.
    /// </summary>
    private sealed class FakeSerializer : ISerializer
    {
        private readonly Message? _result;
        private readonly Exception? _exception;

        public FakeSerializer(Message result) => _result = result;

        public FakeSerializer(Exception exception) => _exception = exception;

        public ValueTask<Message> DeserializeAsync(TransportMessage transportMessage, Type? valueType)
        {
            if (_exception != null)
            {
                return ValueTask.FromException<Message>(_exception);
            }

            return new ValueTask<Message>(_result!);
        }

        // Remaining members are not used by RecordingTransport
        public string Serialize(Message message) => throw new NotSupportedException();

        public ValueTask<TransportMessage> SerializeToTransportMessageAsync(Message message) =>
            throw new NotSupportedException();

        public Message? Deserialize(string json) => throw new NotSupportedException();

        public object? Deserialize(object value, Type valueType) => throw new NotSupportedException();

        public bool IsJsonType(object jsonObject) => throw new NotSupportedException();
    }

    /// <summary>Stub <see cref="ITransport"/> that always returns a fixed <see cref="OperateResult"/>.</summary>
    private sealed class FakeTransport(OperateResult result) : ITransport
    {
        public int CallCount { get; private set; }

        public BrokerAddress BrokerAddress => new();

        public Task<OperateResult> SendAsync(TransportMessage message, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(result);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>
    /// Stub <see cref="IConsumeExecutionPipeline"/> that either returns a fixed result or throws a fixed exception.
    /// </summary>
    private sealed class FakePipeline : IConsumeExecutionPipeline
    {
        private readonly ConsumerExecutedResult? _result;
        private readonly Exception? _exception;

        public int CallCount { get; private set; }

        public FakePipeline(ConsumerExecutedResult result) => _result = result;

        public FakePipeline(Exception exception) => _exception = exception;

        public Task<ConsumerExecutedResult> ExecuteAsync(
            ConsumerContext context,
            object messageInstance,
            Type messageType,
            CancellationToken cancellationToken = default
        )
        {
            CallCount++;

            if (_exception != null)
            {
                throw _exception;
            }

            return Task.FromResult(_result!);
        }
    }

    // ─── fixtures ────────────────────────────────────────────────────────────

    private sealed class SimplePayload
    {
        public string? Value { get; set; }
    }
}
