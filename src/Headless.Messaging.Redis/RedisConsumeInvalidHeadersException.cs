// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Redis;

/// <summary>
/// Thrown when a Redis stream entry's headers field exists but cannot be deserialized into the
/// expected message header format.
/// </summary>
/// <param name="entryId">The Redis stream entry identifier the parse failure originated from.</param>
/// <param name="ex">The underlying deserialization failure.</param>
[PublicAPI]
public sealed class RedisConsumeInvalidHeadersException(string entryId, Exception ex)
    : Exception($"Redis entry [{entryId}] has not headers that are formatted properly as message headers.", ex);
