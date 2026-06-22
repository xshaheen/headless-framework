// Copyright (c) Mahmoud Shaheen. All rights reserved.

using StackExchange.Redis;

namespace Headless.Messaging.Redis;

/// <summary>
/// Thrown when a Redis stream entry is consumed but does not contain the expected message headers field.
/// </summary>
public class RedisConsumeMissingHeadersException(StreamEntry entry)
    : Exception(message: $"Redis entry [{entry.Id}] is missing message headers.");

/// <summary>
/// Thrown when a Redis stream entry is consumed but does not contain the expected message body field.
/// </summary>
public class RedisConsumeMissingBodyException(StreamEntry entry)
    : Exception(message: $"Redis entry [{entry.Id}] is missing message body.");

/// <summary>
/// Thrown when a Redis stream entry's headers field exists but cannot be deserialized into the
/// expected message header format.
/// </summary>
public class RedisConsumeInvalidHeadersException(StreamEntry entry, Exception ex)
    : Exception(
        message: $"Redis entry [{entry.Id}] has not headers that are formatted properly as message headers.",
        ex
    );

/// <summary>
/// Thrown when a Redis stream entry's body field exists but cannot be deserialized into the
/// expected message body format.
/// </summary>
public class RedisConsumeInvalidBodyException(StreamEntry entry, Exception ex)
    : Exception(message: $"Redis entry [{entry.Id}] has not body that is formatted properly as message body.", ex);
