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
    /// <summary>
    /// Gets the image the fixture runs, <see cref="TestImages.Redis"/> unless overridden. Override it to run another
    /// version or a variant of the same server.
    /// </summary>
    protected virtual string Image => TestImages.Redis;

    protected override RedisBuilder Configure()
    {
        return base.Configure().WithImage(Image).WithHostPingWaitStrategy();
    }

    /// <summary>Deletes every key in every database of the shared container.</summary>
    /// <remarks>The container is shared by every test in the collection, so call this from setup or teardown to start clean.</remarks>
    public Task FlushAllAsync(CancellationToken cancellationToken = default)
    {
        return Container.FlushAllAsync(cancellationToken);
    }
}
