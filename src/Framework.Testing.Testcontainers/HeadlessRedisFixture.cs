// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Testcontainers.Redis;
using Testcontainers.Xunit;

namespace Framework.Testing.Testcontainers;

public class HeadlessRedisFixture() : ContainerFixture<RedisBuilder, RedisContainer>(TestContextMessageSink.Instance)
{
    protected override RedisBuilder Configure()
    {
        return base.Configure().WithImage("redis:7-alpine");
    }
}
