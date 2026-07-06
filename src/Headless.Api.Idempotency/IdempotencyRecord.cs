// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Api.Idempotency;

internal enum RecordKind
{
    InFlight = 0,
    Complete = 1,
}

internal sealed class IdempotencyRecord : IEquatable<IdempotencyRecord>
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

    /// <summary>
    /// Value equality: equal Kind, StatusCode, CreatedAt, Body content, Fingerprint content,
    /// and Headers (key sets and per-key value sequences). Used by the middleware's finalize
    /// path via <see cref="Headless.Caching.ICache.TryReplaceIfEqualAsync{T}"/> to turn the
    /// "re-check then upsert" pair into a single CAS round trip — needed for the in-memory
    /// cache because <c>Equals(currentValue, expected)</c> falls back to reference equality
    /// without an <see cref="IEquatable{T}"/> override, and the round-tripped marker is a
    /// different reference than the one we inserted.
    /// </summary>
    public bool Equals(IdempotencyRecord? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return Kind == other.Kind
            && StatusCode == other.StatusCode
            && CreatedAt == other.CreatedAt
            && _BytesEqual(Body, other.Body)
            && _BytesEqual(Fingerprint, other.Fingerprint)
            && _HeadersEqual(Headers, other.Headers);
    }

    public override bool Equals(object? obj) => obj is IdempotencyRecord other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(Kind, StatusCode, CreatedAt, Body.Length, Fingerprint?.Length ?? -1, Headers.Count);

    private static bool _BytesEqual(byte[]? a, byte[]? b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a is null || b is null)
        {
            return false;
        }

        return a.AsSpan().SequenceEqual(b);
    }

    private static bool _HeadersEqual(Dictionary<string, string[]> a, Dictionary<string, string[]> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        foreach (var (key, aValues) in a)
        {
            if (!b.TryGetValue(key, out var bValues))
            {
                return false;
            }

            if (!aValues.AsSpan().SequenceEqual(bValues.AsSpan()))
            {
                return false;
            }
        }

        return true;
    }
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

    public override void Write(Utf8JsonWriter writer, Dictionary<string, string[]> value, JsonSerializerOptions options)
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
