// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Headless.Idempotency.Caching;

// Absent fields are omitted rather than written as null: the generated serializer writes a null byte array as an empty
// base64 string, which reads back as an empty result instead of no result.
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CacheIdempotencyEntry))]
internal sealed partial class CacheIdempotencyJsonContext : JsonSerializerContext;
