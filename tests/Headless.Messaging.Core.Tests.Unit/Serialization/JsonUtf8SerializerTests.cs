// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;
using System.Text.Json;
using Framework.Testing.Tests;
using Headless.Messaging.Configuration;
using Headless.Messaging.Messages;
using Headless.Messaging.Serialization;
using Microsoft.Extensions.Options;

namespace Tests.Serialization;

public sealed class JsonUtf8SerializerTests : TestBase
{
    private readonly JsonUtf8Serializer _serializer;

    public JsonUtf8SerializerTests()
    {
        var options = Options.Create(new MessagingOptions());
        _serializer = new JsonUtf8Serializer(options);
    }

    [Fact]
    public async Task should_serialize_message_to_utf8()
    {
        // given
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            { "headless-msg-id", "123" },
            { "headless-msg-name", "test.topic" },
        };
        var message = new Message(headers, new TestPayload { Name = "Test", Value = 42 });

        // when
        var transport = await _serializer.SerializeToTransportMessageAsync(message);

        // then
        transport.Headers.Should().BeSameAs(headers);
        transport.Body.Length.Should().BeGreaterThan(0);

        // verify it's valid JSON
        var json = Encoding.UTF8.GetString(transport.Body.Span);
        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("Name").GetString().Should().Be("Test");
        doc.RootElement.GetProperty("Value").GetInt32().Should().Be(42);
    }

    [Fact]
    public async Task should_deserialize_utf8_to_message()
    {
        // given
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            { "headless-msg-id", "456" },
            { "headless-msg-name", "test.topic" },
        };
        var json = """{"Name":"Deserialized","Value":99}"""u8.ToArray();
        var transport = new TransportMessage(headers, json);

        // when
        var message = await _serializer.DeserializeAsync(transport, typeof(TestPayload));

        // then
        message.Headers.Should().BeSameAs(headers);
        message.Value.Should().NotBeNull();
        var payload = message.Value.Should().BeOfType<TestPayload>().Which;
        payload.Name.Should().Be("Deserialized");
        payload.Value.Should().Be(99);
    }

    [Fact]
    public async Task should_handle_null_message_value()
    {
        // given
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            { "headless-msg-id", "789" },
        };
        var message = new Message(headers, value: null);

        // when
        var transport = await _serializer.SerializeToTransportMessageAsync(message);

        // then
        transport.Headers.Should().BeSameAs(headers);
        transport.Body.Length.Should().Be(0);
    }

    [Fact]
    public async Task should_handle_empty_body_on_deserialize()
    {
        // given
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            { "headless-msg-id", "empty" },
        };
        var transport = new TransportMessage(headers, ReadOnlyMemory<byte>.Empty);

        // when
        var message = await _serializer.DeserializeAsync(transport, typeof(TestPayload));

        // then
        message.Headers.Should().BeSameAs(headers);
        message.Value.Should().BeNull();
    }

    [Fact]
    public async Task should_handle_null_value_type_on_deserialize()
    {
        // given
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal);
        var json = """{"Name":"Test"}"""u8.ToArray();
        var transport = new TransportMessage(headers, json);

        // when
        var message = await _serializer.DeserializeAsync(transport, valueType: null);

        // then
        message.Value.Should().BeNull();
    }

    [Fact]
    public async Task should_preserve_message_headers()
    {
        // given
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            { "headless-msg-id", "preserve-test" },
            { "headless-msg-name", "test.topic" },
            { "headless-corr-id", "correlation-123" },
            { "custom-header", "custom-value" },
        };
        var message = new Message(headers, new TestPayload { Name = "RoundTrip" });

        // when - serialize then deserialize
        var transport = await _serializer.SerializeToTransportMessageAsync(message);
        var deserializedMessage = await _serializer.DeserializeAsync(transport, typeof(TestPayload));

        // then - headers should be preserved
        deserializedMessage.Headers.Should().ContainKey("headless-msg-id").WhoseValue.Should().Be("preserve-test");
        deserializedMessage.Headers.Should().ContainKey("headless-msg-name").WhoseValue.Should().Be("test.topic");
        deserializedMessage.Headers.Should().ContainKey("headless-corr-id").WhoseValue.Should().Be("correlation-123");
        deserializedMessage.Headers.Should().ContainKey("custom-header").WhoseValue.Should().Be("custom-value");
    }

    [Fact]
    public void should_serialize_message_to_string()
    {
        // given
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            { "headless-msg-id", "string-test" },
        };
        var message = new Message(headers, new TestPayload { Name = "StringSerialize" });

        // when
        var json = _serializer.Serialize(message);

        // then
        json.Should().NotBeNullOrEmpty();
        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("Headers").GetProperty("headless-msg-id").GetString().Should().Be("string-test");
    }

    [Fact]
    public void should_deserialize_message_from_string()
    {
        // given
        var json = """
        {
            "Headers": { "headless-msg-id": "from-string" },
            "Value": { "Name": "FromString", "Value": 123 }
        }
        """;

        // when
        var message = _serializer.Deserialize(json);

        // then
        message.Should().NotBeNull();
        message!.Headers["headless-msg-id"].Should().Be("from-string");
        _serializer.IsJsonType(message.Value!).Should().BeTrue();
    }

    [Fact]
    public void should_deserialize_json_element()
    {
        // given
        var json = """{"Name":"FromElement","Value":456}""";
        var doc = JsonDocument.Parse(json);
        var element = doc.RootElement.Clone();

        // when
        var result = _serializer.Deserialize(element, typeof(TestPayload));

        // then
        result.Should().NotBeNull();
        var payload = result.Should().BeOfType<TestPayload>().Which;
        payload.Name.Should().Be("FromElement");
        payload.Value.Should().Be(456);
    }

    [Fact]
    public void should_throw_for_non_json_element()
    {
        // given
        var notJsonElement = new { Name = "Test" };

        // when
        var act = () => _serializer.Deserialize(notJsonElement, typeof(TestPayload));

        // then
        act.Should().Throw<NotSupportedException>().WithMessage("*JsonElement*");
    }

    [Fact]
    public void should_identify_json_type()
    {
        // given
        var json = """{"Name":"Test"}""";
        var doc = JsonDocument.Parse(json);
        var element = doc.RootElement.Clone();

        // when/then
        _serializer.IsJsonType(element).Should().BeTrue();
        _serializer.IsJsonType(new { Name = "Test" }).Should().BeFalse();
        _serializer.IsJsonType("string").Should().BeFalse();
        _serializer.IsJsonType(123).Should().BeFalse();
    }

    [Fact]
    public async Task should_handle_complex_nested_object()
    {
        // given
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal);
        var complexPayload = new ComplexPayload
        {
            Id = Guid.NewGuid(),
            Created = DateTime.UtcNow,
            Nested = new NestedPayload { Items = ["a", "b", "c"] },
        };
        var message = new Message(headers, complexPayload);

        // when
        var transport = await _serializer.SerializeToTransportMessageAsync(message);
        var deserialized = await _serializer.DeserializeAsync(transport, typeof(ComplexPayload));

        // then
        deserialized.Value.Should().NotBeNull();
        var result = deserialized.Value.Should().BeOfType<ComplexPayload>().Which;
        result.Id.Should().Be(complexPayload.Id);
        result.Nested.Should().NotBeNull();
        result.Nested!.Items.Should().Equal("a", "b", "c");
    }

    [Fact]
    public async Task should_throw_when_serializing_null_message()
    {
        // given
        Message? message = null;

        // when
        var act = async () => await _serializer.SerializeToTransportMessageAsync(message!);

        // then
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    private sealed class TestPayload
    {
        public string? Name { get; init; }
        public int Value { get; init; }
    }

    private sealed class ComplexPayload
    {
        public Guid Id { get; init; }
        public DateTime Created { get; init; }
        public NestedPayload? Nested { get; init; }
    }

    private sealed class NestedPayload
    {
        public List<string> Items { get; init; } = [];
    }
}
