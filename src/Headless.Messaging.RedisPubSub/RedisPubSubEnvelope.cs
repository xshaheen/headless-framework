// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;

namespace Headless.Messaging.RedisPubSub;

internal static class RedisPubSubEnvelope
{
    public static string Serialize(TransportMessage message)
    {
        var envelope = new Envelope(message.Headers, Convert.ToBase64String(message.Body.Span));
        return JsonSerializer.Serialize(envelope);
    }

    public static TransportMessage Deserialize(string value)
    {
        var envelope =
            JsonSerializer.Deserialize<Envelope>(value)
            ?? throw new InvalidOperationException("Redis Pub/Sub message envelope is empty.");

        var body = string.IsNullOrEmpty(envelope.BodyBase64)
            ? ReadOnlyMemory<byte>.Empty
            : Convert.FromBase64String(envelope.BodyBase64);

        return new TransportMessage(envelope.Headers, body);
    }

    private sealed record Envelope(IDictionary<string, string?> Headers, string BodyBase64);
}
