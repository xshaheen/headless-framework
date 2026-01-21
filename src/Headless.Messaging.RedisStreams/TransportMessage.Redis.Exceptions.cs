// Copyright (c) Mahmoud Shaheen. All rights reserved.

using StackExchange.Redis;

namespace Headless.Messaging.RedisStreams;

public class RedisConsumeMissingHeadersException(StreamEntry entry)
    : Exception(message: $"Redis entry [{entry.Id}] is missing message headers.");

public class RedisConsumeMissingBodyException(StreamEntry entry)
    : Exception(message: $"Redis entry [{entry.Id}] is missing message body.");

public class RedisConsumeInvalidHeadersException(StreamEntry entry, Exception ex)
    : Exception(
        message: $"Redis entry [{entry.Id}] has not headers that are formatted properly as message headers.",
        ex
    );

public class RedisConsumeInvalidBodyException(StreamEntry entry, Exception ex)
    : Exception(message: $"Redis entry [{entry.Id}] has not body that is formatted properly as message body.", ex);
