// Copyright (c) Mahmoud Shaheen. All rights reserved.

using DotNet.Testcontainers.Builders;
using Testcontainers.Nats;
using Testcontainers.Xunit;
using Xunit;

namespace Headless.Testing.Testcontainers;

/// <summary>
/// Shared NATS container fixture pinned to <see cref="TestImages.Nats"/>.
/// Subclass to add per-project JetStream configuration or stream definitions.
/// </summary>
[PublicAPI]
public class HeadlessNatsFixture() : ContainerFixture<NatsBuilder, NatsContainer>(TestContextMessageSink.Instance)
{
    /// <summary>
    /// Gets the image the fixture runs, <see cref="TestImages.Nats"/> unless overridden. Override it to run another
    /// version or a variant of the same server.
    /// </summary>
    protected virtual string Image => TestImages.Nats;

    protected override NatsBuilder Configure()
    {
        return base.Configure()
            .WithImage(Image)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Server is ready"));
    }

    /// <inheritdoc />
    protected override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync().ConfigureAwait(false);
        await ContainerHostPort
            .EnsureReachableAsync(
                Container,
                NatsBuilder.NatsClientPort,
                cancellationToken: TestContext.Current.CancellationToken
            )
            .ConfigureAwait(false);
    }
}
