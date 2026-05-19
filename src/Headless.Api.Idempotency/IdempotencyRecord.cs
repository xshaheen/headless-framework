// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Headless.Api.Idempotency;

internal enum RecordKind
{
    InFlight = 0,
    Complete = 1,
}

internal sealed class IdempotencyRecord
{
    public RecordKind Kind { get; init; }
    public int StatusCode { get; init; }

    /// <summary>
    /// Allowlisted response headers captured at completion time. The dictionary is built with
    /// <see cref="StringComparer.OrdinalIgnoreCase"/> so callers can look up
    /// <c>"content-type"</c> regardless of the original casing; the custom converter preserves
    /// that comparer across JSON round-trips.
    /// </summary>
    [JsonConverter(typeof(OrdinalIgnoreCaseHeadersJsonConverter))]
    public Dictionary<string, string[]> Headers { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public byte[] Body { get; init; } = [];
    public byte[]? Fingerprint { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
/// Reads/writes the <see cref="IdempotencyRecord.Headers"/> dictionary while preserving the
/// <see cref="StringComparer.OrdinalIgnoreCase"/> comparer across serialization round-trips.
/// Default <see cref="JsonSerializer"/> behavior reconstructs <see cref="Dictionary{TKey,TValue}"/>
/// with the default ordinal comparer, which would silently break case-insensitive header lookups
/// after a cache hydration.
/// </summary>
internal sealed class OrdinalIgnoreCaseHeadersJsonConverter : JsonConverter<Dictionary<string, string[]>>
{
    public override Dictionary<string, string[]> Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected start of object for headers dictionary.");
        }

        var result = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return result;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("Expected property name in headers dictionary.");
            }

            var key = reader.GetString()!;
            reader.Read();
            var values = JsonSerializer.Deserialize<string[]>(ref reader, options) ?? [];
            result[key] = values;
        }

        throw new JsonException("Unexpected end of JSON while reading headers dictionary.");
    }

    public override void Write(
        Utf8JsonWriter writer,
        Dictionary<string, string[]> value,
        JsonSerializerOptions options
    )
    {
        writer.WriteStartObject();
        foreach (var (key, values) in value)
        {
            writer.WritePropertyName(key);
            JsonSerializer.Serialize(writer, values, options);
        }
        writer.WriteEndObject();
    }
}
