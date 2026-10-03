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
            new NameValueEntry(_Body, _ToJson(message.Body.ToArray())),
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
        byte[]? body;

        var streamDict = streamEntry.Values.ToDictionary(c => c.Name, c => c.Value);

        var entryId = streamEntry.Id.ToString();

        if (!streamDict.TryGetValue(_Headers, out var headersRaw) || headersRaw.IsNullOrEmpty)
        {
            throw new RedisConsumeMissingHeadersException(entryId);
        }

        if (!streamDict.TryGetValue(_Body, out var bodyRaw))
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

        if (!bodyRaw.IsNullOrEmpty)
        {
            try
            {
                body = JsonSerializer.Deserialize<byte[]>(json: bodyRaw!, _JsonOptions);
            }
            catch (Exception ex)
            {
                throw new RedisConsumeInvalidBodyException(entryId, ex);
            }
        }
        else
        {
            body = null;
        }

        _ValidateRequiredHeaders(headers, entryId);
        return new TransportMessage(headers, body);
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
