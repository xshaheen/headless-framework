// Copyright (c) Mahmoud Shaheen. All rights reserved.

using StackExchange.Redis;

namespace Headless.Messaging.Redis;

internal static class RedisMessage
{
    private const string _Headers = "headers";
    private const string _Body = "body";
    private static readonly JsonSerializerOptions _JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly long _MaxUnixMilliseconds = DateTimeOffset.MaxValue.ToUnixTimeMilliseconds();

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

    /// <summary>Reads the <c>headers</c> and <c>body</c> fields of a stream entry.</summary>
    /// <param name="streamEntry">The entry read from the stream.</param>
    /// <param name="headersOptional">
    /// Whether the entry may omit the <c>headers</c> field, as an entry a foreign producer wrote to a raw route does.
    /// An absent field then reads as no headers; a present one must still be a JSON object of strings.
    /// </param>
    /// <exception cref="RedisConsumeMissingHeadersException">The headers field is required and absent or empty.</exception>
    /// <exception cref="RedisConsumeMissingBodyException">The entry has no body field.</exception>
    /// <exception cref="RedisConsumeInvalidHeadersException">The headers field is not a JSON object of strings.</exception>
    public static TransportMessage Create(StreamEntry streamEntry, bool headersOptional = false)
    {
        Dictionary<string, string?> headers;

        // An entry written by AsStreamEntries carries exactly two fields; a per-entry ToDictionary allocated a
        // Dictionary (plus boxing) just to look them up by name.
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

        if (headersRaw.IsNullOrEmpty && !headersOptional)
        {
            throw new RedisConsumeMissingHeadersException(entryId);
        }

        if (bodyRaw.IsNull)
        {
            throw new RedisConsumeMissingBodyException(entryId);
        }

        if (headersRaw.IsNullOrEmpty)
        {
            headers = new Dictionary<string, string?>(StringComparer.Ordinal);
        }
        else
        {
            try
            {
                headers =
                    JsonSerializer.Deserialize<Dictionary<string, string?>>(json: headersRaw!, _JsonOptions)
                    ?? throw new JsonException("The Redis headers field is JSON null.");
            }
            catch (Exception ex)
            {
                throw new RedisConsumeInvalidHeadersException(entryId, ex);
            }
        }

        return new TransportMessage(headers, (ReadOnlyMemory<byte>)bodyRaw);
    }

    /// <summary>
    /// Reads the time the server stored an entry from its id, <c>&lt;milliseconds&gt;-&lt;sequence&gt;</c>, or
    /// <see langword="null"/> when the producer chose an id that does not carry one.
    /// </summary>
    public static DateTimeOffset? StoredAt(string entryId)
    {
        var dash = entryId.IndexOf('-', StringComparison.Ordinal);
        var milliseconds = dash < 0 ? entryId.AsSpan() : entryId.AsSpan(0, dash);

        if (
            !long.TryParse(milliseconds, NumberStyles.None, CultureInfo.InvariantCulture, out var unixMilliseconds)
            || unixMilliseconds > _MaxUnixMilliseconds
        )
        {
            return null;
        }

        return DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds);
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
