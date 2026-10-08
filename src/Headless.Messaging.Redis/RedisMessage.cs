// Copyright (c) Mahmoud Shaheen. All rights reserved.

using StackExchange.Redis;

namespace Headless.Messaging.Redis;

internal static class RedisMessage
{
    private const string _Headers = "headers";
    private const string _Body = "body";
    private static readonly JsonSerializerOptions _JsonOptions = new(JsonSerializerDefaults.Web);

    public static NameValueEntry[] AsStreamEntries(this TransportMessage message)
    {
        return
        [
            new NameValueEntry(_Headers, _ToJson(message.Headers)),
            // Raw bytes: a stream field is binary-safe, so base64 inside JSON only cost a third more space and a decode.
            new NameValueEntry(_Body, message.Body),
        ];
    }

    /// <summary>
    /// Encodes a reply as one pub/sub payload: a JSON object holding the headers and the body, which a stream entry
    /// would hold as two fields.
    /// </summary>
    public static byte[] AsReplyPayload(this TransportMessage reply)
    {
        return JsonSerializer.SerializeToUtf8Bytes(
            new RedisReplyPayload(reply.Headers, reply.Body.ToArray()),
            _JsonOptions
        );
    }

    /// <summary>Decodes a payload written by <see cref="AsReplyPayload"/>.</summary>
    /// <exception cref="JsonException">The payload is not a reply payload.</exception>
    public static TransportMessage CreateReply(ReadOnlySpan<byte> payload)
    {
        var decoded =
            JsonSerializer.Deserialize<RedisReplyPayload>(payload, _JsonOptions)
            ?? throw new JsonException("The Redis reply payload is null.");

        if (decoded.Headers is null)
        {
            throw new JsonException("The Redis reply payload has no headers.");
        }

        return new TransportMessage(
            new Dictionary<string, string?>(decoded.Headers, StringComparer.Ordinal),
            decoded.Body
        );
    }

    public static TransportMessage Create(StreamEntry streamEntry)
    {
        Dictionary<string, string?> headers;

        // The entry always carries exactly the two fields written by AsStreamEntries; a per-entry
        // ToDictionary allocated a Dictionary (plus boxing) just to look them up by name.
        RedisValue headersRaw = RedisValue.Null;
        RedisValue bodyRaw = RedisValue.Null;

        foreach (var field in streamEntry.Values)
        {
            if (field.Name == _Headers)
            {
                headersRaw = field.Value;
            }
            else if (field.Name == _Body)
            {
                bodyRaw = field.Value;
            }
        }

        var entryId = streamEntry.Id.ToString();

        if (headersRaw.IsNullOrEmpty)
        {
            throw new RedisConsumeMissingHeadersException(entryId);
        }

        if (bodyRaw.IsNull)
        {
            throw new RedisConsumeMissingBodyException(entryId);
        }

        try
        {
            headers = JsonSerializer.Deserialize<Dictionary<string, string?>>(json: headersRaw!, _JsonOptions)!;
        }
        catch (Exception ex)
        {
            throw new RedisConsumeInvalidHeadersException(entryId, ex);
        }

        _ValidateRequiredHeaders(headers, entryId);
        return new TransportMessage(headers, (ReadOnlyMemory<byte>)bodyRaw);
    }

    private static void _ValidateRequiredHeaders(Dictionary<string, string?> headers, string entryId)
    {
        if (
            !headers.TryGetValue(Headers.MessageId, out var messageId)
            || string.IsNullOrWhiteSpace(messageId)
            || !headers.TryGetValue(Headers.MessageName, out var messageName)
            || string.IsNullOrWhiteSpace(messageName)
        )
        {
            throw new RedisConsumeInvalidHeadersException(
                entryId,
                new InvalidDataException("The Redis transport envelope is missing a required Messaging header.")
            );
        }
    }

    private static RedisValue _ToJson(object? obj)
    {
        if (obj == null)
        {
            return RedisValue.Null;
        }

        return JsonSerializer.Serialize(obj, _JsonOptions);
    }
}

internal sealed record RedisReplyPayload(IDictionary<string, string?>? Headers, byte[]? Body);
