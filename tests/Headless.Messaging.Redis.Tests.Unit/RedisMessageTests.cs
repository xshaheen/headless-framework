// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Redis;
using Headless.Testing.Tests;
using StackExchange.Redis;

namespace Tests;

/// <summary>
/// Unit tests for <see cref="RedisMessage"/> (TransportMessage.Redis.cs).
/// </summary>
public sealed class RedisMessageTests : TestBase
{
    private static readonly JsonSerializerOptions _WebJsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void should_convert_transport_message_to_stream_entries()
    {
        // given
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
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
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
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
    public void should_store_body_as_raw_bytes()
    {
        // given
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = "id",
            [Headers.MessageName] = "name",
        };
        var body = new byte[] { 1, 2, 3, 4, 5 };
        var message = new TransportMessage(headers, body);

        // when
        var entries = message.AsStreamEntries();
        var bodyEntry = entries.First(e => e.Name == "body");

        // then
        ((byte[])bodyEntry.Value!)
            .Should()
            .Equal(1, 2, 3, 4, 5);
    }

    [Fact]
    public void should_round_trip_a_binary_body_through_stream_entries()
    {
        // given
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = "id",
            [Headers.MessageName] = "name",
        };
        byte[] body = [0, 255, 34, 92, 0, 128];
        var entries = new TransportMessage(headers, body).AsStreamEntries();

        // when
        var message = RedisMessage.Create(new StreamEntry("1-0", entries));

        // then
        message.Body.ToArray().Should().Equal(body);
    }

    [Fact]
    public void should_create_transport_message_from_stream_entry()
    {
        // given
        var headersJson = JsonSerializer.Serialize(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.MessageId] = "msg-789",
                [Headers.MessageName] = "users.updated",
            }
        );
        var values = new NameValueEntry[] { new("headers", headersJson), new("body", new byte[] { 10, 20, 30 }) };
        var streamEntry = new StreamEntry("1234567-0", values);

        // when
        var message = RedisMessage.Create(streamEntry);

        // then
        message.Id.Should().Be("msg-789");
        message.Name.Should().Be("users.updated");
        message.Body.ToArray().Should().BeEquivalentTo([10, 20, 30]);
    }

    [Fact]
    public void should_not_stamp_consumer_identity_on_the_entry()
    {
        // given
        var headersJson = JsonSerializer.Serialize(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.MessageId] = "id",
                [Headers.MessageName] = "name",
            }
        );
        var values = new NameValueEntry[] { new("headers", headersJson), new("body", RedisValue.EmptyString) };
        var streamEntry = new StreamEntry("1234567-0", values);

        // when
        var message = RedisMessage.Create(streamEntry);

        // then — the messaging core stamps the identity once the delivery is routed to a consumer
        message.GetConsumerIdentity().Should().BeNull();
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
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.MessageId] = "id",
                [Headers.MessageName] = "name",
            }
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
    public void should_handle_empty_body()
    {
        // given
        var headersJson = JsonSerializer.Serialize(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.MessageId] = "id",
                [Headers.MessageName] = "name",
            }
        );
        var values = new NameValueEntry[] { new("headers", headersJson), new("body", RedisValue.EmptyString) };
        var streamEntry = new StreamEntry("1234567-0", values);

        // when
        var message = RedisMessage.Create(streamEntry);

        // then
        message.Body.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void should_read_no_headers_when_optional_headers_are_absent()
    {
        // given
        var streamEntry = new StreamEntry("1234567-0", [new NameValueEntry("body", "{}")]);

        // when
        var message = RedisMessage.Create(streamEntry, headersOptional: true);

        // then
        message.Headers.Should().BeEmpty();
        message.Body.ToArray().Should().Equal("{}"u8.ToArray());
    }

    [Fact]
    public void should_throw_when_optional_headers_are_present_and_invalid_json()
    {
        // given
        var values = new NameValueEntry[] { new("headers", "not-valid-json"), new("body", "{}") };
        var streamEntry = new StreamEntry("1234567-0", values);

        // when & then
        var action = () => RedisMessage.Create(streamEntry, headersOptional: true);
        action.Should().ThrowExactly<RedisConsumeInvalidHeadersException>();
    }

    [Fact]
    public void should_throw_when_headers_are_json_null()
    {
        // given
        var values = new NameValueEntry[] { new("headers", "null"), new("body", "{}") };
        var streamEntry = new StreamEntry("1234567-0", values);

        // when & then
        var action = () => RedisMessage.Create(streamEntry, headersOptional: true);
        action.Should().ThrowExactly<RedisConsumeInvalidHeadersException>();
    }

    [Fact]
    public void should_throw_when_optional_headers_are_absent_and_body_is_missing()
    {
        // given
        var streamEntry = new StreamEntry("1234567-0", [new NameValueEntry("payload", "{}")]);

        // when & then
        var action = () => RedisMessage.Create(streamEntry, headersOptional: true);
        action.Should().ThrowExactly<RedisConsumeMissingBodyException>();
    }

    [Fact]
    public void should_write_headers_bytes_identical_to_the_utf8_json_string_when_headers_hold_non_ascii_quotes_and_nulls()
    {
        // given
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = "msg-\u00e9\u4e2d\ud83d\ude00",
            [Headers.MessageName] = "orders.\"quoted\"",
            ["x-arabic"] = "\u0645\u0631\u062d\u0628\u0627",
            ["x-null"] = null,
        };
        var expected = Encoding.UTF8.GetBytes(JsonSerializer.Serialize<object>(headers, _WebJsonOptions));
        var message = new TransportMessage(headers, ReadOnlyMemory<byte>.Empty);

        // when
        var headersEntry = message.AsStreamEntries().First(e => e.Name == "headers");

        // then
        ((byte[])headersEntry.Value!)
            .Should()
            .Equal(expected);
    }

    [Fact]
    public void should_round_trip_headers_when_creating_from_the_written_stream_entries()
    {
        // given
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = "msg-\u00e9",
            [Headers.MessageName] = "orders.\"quoted\"",
            ["x-null"] = null,
        };
        var entries = new TransportMessage(headers, new byte[] { 7 }).AsStreamEntries();

        // when
        var message = RedisMessage.Create(new StreamEntry("1234567-0", entries));

        // then
        message.Headers.Should().BeEquivalentTo(headers);
        message.Body.ToArray().Should().Equal(7);
    }

    [Theory]
    [InlineData("1791547200000-3", 1_791_547_200_000L)]
    [InlineData("0-1", 0L)]
    public void should_read_the_stored_time_from_the_entry_id(string entryId, long unixMilliseconds)
    {
        // when
        var storedAt = RedisMessage.StoredAt(entryId);

        // then
        storedAt.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds));
    }

    [Theory]
    [InlineData("18446744073709551615-0")]
    [InlineData("abc-0")]
    [InlineData("")]
    public void should_read_no_stored_time_when_the_entry_id_carries_none(string entryId)
    {
        // when
        var storedAt = RedisMessage.StoredAt(entryId);

        // then
        storedAt.Should().BeNull();
    }
}
