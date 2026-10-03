// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Redis;

/// <summary>
/// Thrown when a Redis stream entry is consumed but does not contain the expected message headers field.
/// </summary>
/// <param name="entryId">The Redis stream entry identifier the parse failure originated from.</param>
[PublicAPI]
public sealed class RedisConsumeMissingHeadersException(string entryId)
    : Exception($"Redis entry [{entryId}] is missing message headers.");
