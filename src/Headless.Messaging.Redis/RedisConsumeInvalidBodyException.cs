// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Redis;

/// <summary>
/// Thrown when a Redis stream entry's body field exists but cannot be deserialized into the
/// expected message body format.
/// </summary>
/// <param name="entryId">The Redis stream entry identifier the parse failure originated from.</param>
/// <param name="ex">The underlying deserialization failure.</param>
[PublicAPI]
public sealed class RedisConsumeInvalidBodyException(string entryId, Exception ex)
    : Exception($"Redis entry [{entryId}] has not body that is formatted properly as message body.", ex);
