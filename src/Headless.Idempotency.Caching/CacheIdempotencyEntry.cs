// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Headless.Idempotency.Caching;

/// <summary>
/// One idempotency record as the cache stores it: the whole record in one entry, so a single compare-and-swap changes
/// all of it or none of it.
/// </summary>
/// <remarks>
/// The cache holds the JSON text of this record, and every write compares against the exact text a call read, never a
/// re-serialization of it, so the comparison does not depend on serializer determinism. Instants are the application
/// clock's, in UTC. <see cref="HighestGeneration" /> is the last generation ever admitted on the record, kept through a
/// release (which clears <see cref="Generation" />), so a new admission can stay above it even when the store's
/// generation counter was evicted.
/// </remarks>
internal sealed record CacheIdempotencyEntry(
    [property: JsonPropertyName("s")] IdempotencyRecordStatus Status,
    [property: JsonPropertyName("fa")] string FingerprintAlgorithm,
    [property: JsonPropertyName("f")] byte[] Fingerprint,
    [property: JsonPropertyName("g")] long? Generation,
    [property: JsonPropertyName("l")] DateTimeOffset? LeaseExpiresAt,
    [property: JsonPropertyName("r")] byte[]? Result,
    [property: JsonPropertyName("rc")] string? ResultContract,
    [property: JsonPropertyName("u")] DateTimeOffset RetentionUntil,
    [property: JsonPropertyName("p")] string? RecoveryPoint,
    [property: JsonPropertyName("ps")] byte[]? RecoveryState,
    [property: JsonPropertyName("pc")] string? RecoveryContract,
    [property: JsonPropertyName("hg")] long? HighestGeneration
)
{
    public string Serialize()
    {
        return JsonSerializer.Serialize(this, CacheIdempotencyJsonContext.Default.CacheIdempotencyEntry);
    }

    /// <summary>Reads the record stored as <paramref name="json" /> at the cache key <paramref name="key" />.</summary>
    /// <exception cref="InvalidOperationException">The text is not an idempotency record.</exception>
    public static CacheIdempotencyEntry Deserialize(string key, string json)
    {
        try
        {
            return JsonSerializer.Deserialize(json, CacheIdempotencyJsonContext.Default.CacheIdempotencyEntry)
                ?? throw new JsonException("The entry is JSON null.");
        }
        catch (JsonException e)
        {
            throw new InvalidOperationException(
                $"The cache entry '{key}' is not an idempotency record; another writer uses this key prefix.",
                e
            );
        }
    }
}

// Absent fields are omitted rather than written as null: the generated serializer writes a null byte array as an empty
// base64 string, which reads back as an empty result instead of no result.
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CacheIdempotencyEntry))]
internal sealed partial class CacheIdempotencyJsonContext : JsonSerializerContext;
