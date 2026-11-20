// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Testcontainers.Redis;
using Testcontainers.Xunit;
using Xunit;

namespace Headless.Testing.Testcontainers;

/// <summary>
/// Shared Redis container fixture pinned to <see cref="TestImages.Redis"/>.
/// Subclass to add per-project setup (e.g., connection multiplexers, script loaders).
/// </summary>
/// <remarks>
/// The fixture starts once the server answers through the published port with its own <c>run_id</c>, and starts the
/// container again on another port while another listener holds that port on the host.
/// </remarks>
[PublicAPI]
public class HeadlessRedisFixture() : ContainerFixture<RedisBuilder, RedisContainer>(TestContextMessageSink.Instance)
{
    /// <summary>
    /// Gets the image the fixture runs, <see cref="TestImages.Redis"/> unless overridden. Override it to run another
    /// version or a variant of the same server.
    /// </summary>
    protected virtual string Image => TestImages.Redis;

    /// <summary>
    /// Gets the check that proves the container's published port reaches this container's server. Start a container
    /// that a test stopped through its <see cref="PublishedPortCheck.StartAsync"/>, so a restart that lands on another
    /// host listener's port moves on.
    /// </summary>
    public PublishedPortCheck PortCheck { get; } = new(RedisBuilder.RedisPort, PublishedPortProbes.RedisRunId);

    protected override RedisBuilder Configure()
    {
        // The module's own in-container ping is the wait; the port check after the start proves the host can reach it.
        return base.Configure().WithImage(Image);
    }

    /// <inheritdoc />
    protected override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync().ConfigureAwait(false);
        await PortCheck
            .VerifyAsync(Container, cancellationToken: TestContext.Current.CancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Deletes every key in every database of the shared container.</summary>
    /// <remarks>The container is shared by every test in the collection, so call this from setup or teardown to start clean.</remarks>
    public Task FlushAllAsync(CancellationToken cancellationToken = default)
    {
        return Container.FlushAllAsync(cancellationToken);
    }
}
