// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Testcontainers.Redis;
using Testcontainers.Xunit;

namespace Headless.Testing.Testcontainers;

/// <summary>
/// Shared Redis container fixture pinned to <see cref="TestImages.Redis"/>.
/// Subclass to add per-project setup (e.g., connection multiplexers, script loaders).
/// </summary>
[PublicAPI]
public class HeadlessRedisFixture() : ContainerFixture<RedisBuilder, RedisContainer>(TestContextMessageSink.Instance)
{
    protected override RedisBuilder Configure()
    {
        return base.Configure().WithImage(TestImages.Redis).WithReuse(true);
    }
}
