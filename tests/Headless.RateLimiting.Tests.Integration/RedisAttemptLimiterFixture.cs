// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Testcontainers;

namespace Tests;

[CollectionDefinition(nameof(RedisAttemptLimiterFixture))]
public sealed class RedisAttemptLimiterFixture : HeadlessRedisFixture, ICollectionFixture<RedisAttemptLimiterFixture>
{
    // allowAdmin lets tests scan and flush the keyspace.
    public string ConnectionString => Container.GetConnectionString() + ",allowAdmin=true";
}
