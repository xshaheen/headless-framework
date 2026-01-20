// Copyright (c) Mahmoud Shaheen. All rights reserved.

using StackExchange.Redis;

namespace Framework.Messages.RedisStreams;

public class RedisConsumeMissingHeadersException(StreamEntry entry)
    : Exception(message: $"Redis entry [{entry.Id}] is missing Cap headers.");

public class RedisConsumeMissingBodyException(StreamEntry entry)
    : Exception(message: $"Redis entry [{entry.Id}] is missing Cap body.");

public class RedisConsumeInvalidHeadersException(StreamEntry entry, Exception ex)
    : Exception(message: $"Redis entry [{entry.Id}] has not headers that are formatted properly as Cap headers.", ex);

public class RedisConsumeInvalidBodyException(StreamEntry entry, Exception ex)
    : Exception(message: $"Redis entry [{entry.Id}] has not body that is formatted properly as Cap body.", ex);
