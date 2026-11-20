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
public class HeadlessNatsFixture : ContainerFixture<NatsBuilder, NatsContainer>
{
    /// <summary>
    /// Gets the image the fixture runs, <see cref="TestImages.Nats"/> unless overridden. Override it to run another
    /// version or a variant of the same server.
    /// </summary>
    protected virtual string Image => TestImages.Nats;

    /// <summary>Creates the fixture.</summary>
    public HeadlessNatsFixture()
        : base(TestContextMessageSink.Instance)
    {
        PortCheck = new(NatsBuilder.NatsClientPort, PublishedPortProbes.NatsServerName(ServerName));
    }

    /// <summary>
    /// Gets the name the server starts with (<c>--server_name</c>), which it greets every client with, so the port check
    /// tells it apart from another NATS server. An override of <see cref="Configure"/> that builds its own
    /// <see cref="NatsBuilder"/> must pass it too.
    /// </summary>
    protected string ServerName { get; } = $"headless-{Guid.NewGuid():N}";

    /// <summary>
    /// Gets the check that proves the container's client port reaches this container's server, by the name it greets
    /// with. Start a container that a test stopped through its <see cref="PublishedPortCheck.StartAsync"/>, so a restart
    /// that lands on another host listener's port moves on.
    /// </summary>
    public PublishedPortCheck PortCheck { get; }

    protected override NatsBuilder Configure()
    {
        return base.Configure()
            .WithImage(Image)
            .WithCommand("--server_name", ServerName)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Server is ready"));
    }

    /// <inheritdoc />
    protected override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync().ConfigureAwait(false);
        await PortCheck
            .VerifyAsync(Container, cancellationToken: TestContext.Current.CancellationToken)
            .ConfigureAwait(false);
    }
}
