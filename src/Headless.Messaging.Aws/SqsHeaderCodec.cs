// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.SQS.Model;

namespace Headless.Messaging.Aws;

/// <summary>Keeps keyed envelopes lossless within SQS's ten-attribute limit.</summary>
internal static class SqsHeaderCodec
{
    internal const string AttributeName = "headless-aws-headers-v1";

    internal static Dictionary<string, MessageAttributeValue> Encode(TransportMessage message)
    {
        if (message.Headers.ContainsKey(AttributeName))
        {
            throw new InvalidOperationException(
                "The headless-aws-headers-v1 attribute is reserved for the SQS envelope."
            );
        }

        if (message.RoutingAffinityKey is not null)
        {
            return new(StringComparer.Ordinal)
            {
                [AttributeName] = new()
                {
                    DataType = "String",
                    StringValue = JsonSerializer.Serialize(message.Headers),
                },
            };
        }

        return message
            .Headers.Where(pair => pair.Value is not null)
            .ToDictionary(
                pair => pair.Key,
                pair => new MessageAttributeValue { DataType = "String", StringValue = pair.Value },
                StringComparer.Ordinal
            );
    }

    internal static Dictionary<string, string?> Decode(IDictionary<string, MessageAttributeValue>? attributes)
    {
        if (attributes is null)
        {
            return new(StringComparer.Ordinal);
        }

        // Earlier publishers allowed arbitrary prefix headers, so only the exact bag attribute selects the new format.
        if (!attributes.TryGetValue(AttributeName, out var bag))
        {
            return attributes.ToDictionary(
                pair => pair.Key,
                pair => (string?)pair.Value.StringValue,
                StringComparer.Ordinal
            );
        }

        if (
            attributes.Count != 1
            || !string.Equals(bag.DataType, "String", StringComparison.Ordinal)
            || bag.StringValue is null
        )
        {
            throw new JsonException("The SQS header bag has mixed attributes or an invalid type.");
        }

        using var document = JsonDocument.Parse(bag.StringValue);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("The SQS header bag must be an object.");
        }

        var headers = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (
                string.Equals(property.Name, AttributeName, StringComparison.Ordinal)
                || property.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null)
                || !headers.TryAdd(property.Name, property.Value.GetString())
            )
            {
                throw new JsonException("The SQS header bag contains a reserved, duplicate, or invalid header.");
            }
        }

        if (!headers.TryGetValue(Headers.RoutingAffinityKey, out var key) || string.IsNullOrWhiteSpace(key))
        {
            throw new JsonException("The SQS keyed envelope is missing its affinity key.");
        }

        return headers;
    }
}
