// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;
using Headless.Messaging.RedisStreams;
using Headless.Testing.Tests;
using StackExchange.Redis;

namespace Tests;

/// <summary>
/// Unit tests for <see cref="RedisMessage"/> (TransportMessage.Redis.cs).
/// </summary>
public sealed class RedisMessageTests : TestBase
{
    [Fact]
    public void should_convert_transport_message_to_stream_entries()
    {
        // given
        var headers = new Dictionary<string, string?>
        {
            [Headers.MessageId] = "msg-123",
            [Headers.MessageName] = "test.topic",
        };
        var body = """{"key":"value"}"""u8.ToArray();
        var message = new TransportMessage(headers, body);

        // when
        var entries = message.AsStreamEntries();

        // then
        entries.Should().HaveCount(2);
        entries.Should().Contain(e => e.Name == "headers");
        entries.Should().Contain(e => e.Name == "body");
    }

    [Fact]
    public void should_serialize_headers_as_json()
    {
        // given
        var headers = new Dictionary<string, string?>
        {
            [Headers.MessageId] = "msg-456",
            [Headers.MessageName] = "orders.created",
            [Headers.CorrelationId] = "corr-xyz",
        };
        var message = new TransportMessage(headers, ReadOnlyMemory<byte>.Empty);

        // when
        var entries = message.AsStreamEntries();
        var headersEntry = entries.First(e => e.Name == "headers");

        // then
        headersEntry.Value.HasValue.Should().BeTrue();
        var jsonString = (string)headersEntry.Value!;
        var deserializedHeaders = JsonSerializer.Deserialize<Dictionary<string, string?>>(jsonString);
        deserializedHeaders.Should().ContainKey(Headers.MessageId);
        deserializedHeaders![Headers.MessageId].Should().Be("msg-456");
    }

    [Fact]
    public void should_serialize_body_as_json()
    {
        // given
        var headers = new Dictionary<string, string?> { [Headers.MessageId] = "id", [Headers.MessageName] = "name" };
        var body = new byte[] { 1, 2, 3, 4, 5 };
        var message = new TransportMessage(headers, body);

        // when
        var entries = message.AsStreamEntries();
        var bodyEntry = entries.First(e => e.Name == "body");

        // then
        bodyEntry.Value.HasValue.Should().BeTrue();
    }

    [Fact]
    public void should_create_transport_message_from_stream_entry()
    {
        // given
        var headersJson = JsonSerializer.Serialize(
            new Dictionary<string, string?> { [Headers.MessageId] = "msg-789", [Headers.MessageName] = "users.updated" }
        );
        var bodyJson = JsonSerializer.Serialize(new byte[] { 10, 20, 30 });

        var values = new NameValueEntry[] { new("headers", headersJson), new("body", bodyJson) };
        var streamEntry = new StreamEntry("1234567-0", values);

        // when
        var message = RedisMessage.Create(streamEntry);

        // then
        message.GetId().Should().Be("msg-789");
        message.GetName().Should().Be("users.updated");
        message.Body.ToArray().Should().BeEquivalentTo([10, 20, 30]);
    }

    [Fact]
    public void should_add_group_to_headers_when_provided()
    {
        // given
        var headersJson = JsonSerializer.Serialize(
            new Dictionary<string, string?> { [Headers.MessageId] = "id", [Headers.MessageName] = "name" }
        );
        var bodyJson = JsonSerializer.Serialize(Array.Empty<byte>());

        var values = new NameValueEntry[] { new("headers", headersJson), new("body", bodyJson) };
        var streamEntry = new StreamEntry("1234567-0", values);

        // when
        var message = RedisMessage.Create(streamEntry, "my-consumer-group");

        // then
        message.GetGroup().Should().Be("my-consumer-group");
    }

    [Fact]
    public void should_not_add_group_when_null_or_empty()
    {
        // given
        var headersJson = JsonSerializer.Serialize(
            new Dictionary<string, string?> { [Headers.MessageId] = "id", [Headers.MessageName] = "name" }
        );
        var bodyJson = JsonSerializer.Serialize(Array.Empty<byte>());

        var values = new NameValueEntry[] { new("headers", headersJson), new("body", bodyJson) };
        var streamEntry = new StreamEntry("1234567-0", values);

        // when
        var message = RedisMessage.Create(streamEntry, null);

        // then
        message.GetGroup().Should().BeNull();
    }

    [Fact]
    public void should_throw_when_headers_missing()
    {
        // given
        var values = new NameValueEntry[] { new("body", "[]") };
        var streamEntry = new StreamEntry("1234567-0", values);

        // when & then
        var action = () => RedisMessage.Create(streamEntry);
        action.Should().ThrowExactly<RedisConsumeMissingHeadersException>();
    }

    [Fact]
    public void should_throw_when_headers_are_empty()
    {
        // given
        var values = new NameValueEntry[] { new("headers", RedisValue.EmptyString), new("body", "[]") };
        var streamEntry = new StreamEntry("1234567-0", values);

        // when & then
        var action = () => RedisMessage.Create(streamEntry);
        action.Should().ThrowExactly<RedisConsumeMissingHeadersException>();
    }

    [Fact]
    public void should_throw_when_body_missing()
    {
        // given
        var headersJson = JsonSerializer.Serialize(
            new Dictionary<string, string?> { [Headers.MessageId] = "id", [Headers.MessageName] = "name" }
        );
        var values = new NameValueEntry[] { new("headers", headersJson) };
        var streamEntry = new StreamEntry("1234567-0", values);

        // when & then
        var action = () => RedisMessage.Create(streamEntry);
        action.Should().ThrowExactly<RedisConsumeMissingBodyException>();
    }

    [Fact]
    public void should_throw_when_headers_invalid_json()
    {
        // given
        var values = new NameValueEntry[] { new("headers", "not-valid-json"), new("body", "[]") };
        var streamEntry = new StreamEntry("1234567-0", values);

        // when & then
        var action = () => RedisMessage.Create(streamEntry);
        action.Should().ThrowExactly<RedisConsumeInvalidHeadersException>();
    }

    [Fact]
    public void should_throw_when_body_invalid_json()
    {
        // given
        var headersJson = JsonSerializer.Serialize(
            new Dictionary<string, string?> { [Headers.MessageId] = "id", [Headers.MessageName] = "name" }
        );
        var values = new NameValueEntry[] { new("headers", headersJson), new("body", "not-valid-json") };
        var streamEntry = new StreamEntry("1234567-0", values);

        // when & then
        var action = () => RedisMessage.Create(streamEntry);
        action.Should().ThrowExactly<RedisConsumeInvalidBodyException>();
    }

    [Fact]
    public void should_handle_empty_body()
    {
        // given
        var headersJson = JsonSerializer.Serialize(
            new Dictionary<string, string?> { [Headers.MessageId] = "id", [Headers.MessageName] = "name" }
        );
        var values = new NameValueEntry[] { new("headers", headersJson), new("body", RedisValue.EmptyString) };
        var streamEntry = new StreamEntry("1234567-0", values);

        // when
        var message = RedisMessage.Create(streamEntry);

        // then
        message.Body.IsEmpty.Should().BeTrue();
    }
}
