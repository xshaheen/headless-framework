// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Redis;

/// <summary>
/// Thrown when a Redis stream entry is consumed but does not contain the expected message headers field.
/// </summary>
/// <param name="entryId">The Redis stream entry identifier the parse failure originated from.</param>
[PublicAPI]
public sealed class RedisConsumeMissingHeadersException(string entryId)
    : Exception($"Redis entry [{entryId}] is missing message headers.");

/// <summary>
/// Thrown when a Redis stream entry is consumed but does not contain the expected message body field.
/// </summary>
/// <param name="entryId">The Redis stream entry identifier the parse failure originated from.</param>
[PublicAPI]
public sealed class RedisConsumeMissingBodyException(string entryId)
    : Exception($"Redis entry [{entryId}] is missing message body.");

/// <summary>
/// Thrown when a Redis stream entry's headers field exists but cannot be deserialized into the
/// expected message header format.
/// </summary>
/// <param name="entryId">The Redis stream entry identifier the parse failure originated from.</param>
/// <param name="ex">The underlying deserialization failure.</param>
[PublicAPI]
public sealed class RedisConsumeInvalidHeadersException(string entryId, Exception ex)
    : Exception($"Redis entry [{entryId}] has not headers that are formatted properly as message headers.", ex);

/// <summary>
/// Thrown when a Redis stream entry's body field exists but cannot be deserialized into the
/// expected message body format.
/// </summary>
/// <param name="entryId">The Redis stream entry identifier the parse failure originated from.</param>
/// <param name="ex">The underlying deserialization failure.</param>
[PublicAPI]
public sealed class RedisConsumeInvalidBodyException(string entryId, Exception ex)
    : Exception($"Redis entry [{entryId}] has not body that is formatted properly as message body.", ex);
