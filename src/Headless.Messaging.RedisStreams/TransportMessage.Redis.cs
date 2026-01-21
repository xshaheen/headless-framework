// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;
using StackExchange.Redis;

namespace Headless.Messaging.RedisStreams;

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

    public static TransportMessage Create(StreamEntry streamEntry, string? groupId = null)
    {
        IDictionary<string, string?> headers;
        byte[]? body;

        var streamDict = streamEntry.Values.ToDictionary(c => c.Name, c => c.Value);

        if (!streamDict.TryGetValue(_Headers, out var headersRaw) || headersRaw.IsNullOrEmpty)
        {
            throw new RedisConsumeMissingHeadersException(streamEntry);
        }

        if (!streamDict.TryGetValue(_Body, out var bodyRaw))
        {
            throw new RedisConsumeMissingBodyException(streamEntry);
        }

        try
        {
            headers = JsonSerializer.Deserialize<IDictionary<string, string?>>(json: headersRaw!, _JsonOptions)!;
        }
        catch (Exception ex)
        {
            throw new RedisConsumeInvalidHeadersException(streamEntry, ex);
        }

        if (!bodyRaw.IsNullOrEmpty)
        {
            try
            {
                body = JsonSerializer.Deserialize<byte[]>(json: bodyRaw!, _JsonOptions);
            }
            catch (Exception ex)
            {
                throw new RedisConsumeInvalidBodyException(streamEntry, ex);
            }
        }
        else
        {
            body = null;
        }

        if (!string.IsNullOrEmpty(groupId))
        {
            headers[Headers.Group] = groupId;
        }

        return new TransportMessage(headers, body);
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
