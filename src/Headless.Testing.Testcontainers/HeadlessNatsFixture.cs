// Copyright (c) Mahmoud Shaheen. All rights reserved.

using DotNet.Testcontainers.Builders;
using Testcontainers.Nats;
using Testcontainers.Xunit;

namespace Headless.Testing.Testcontainers;

/// <summary>
/// Shared NATS container fixture pinned to <see cref="TestImages.Nats"/>.
/// Subclass to add per-project JetStream configuration or stream definitions.
/// </summary>
[PublicAPI]
public class HeadlessNatsFixture() : ContainerFixture<NatsBuilder, NatsContainer>(TestContextMessageSink.Instance)
{
    protected override NatsBuilder Configure()
    {
        return base.Configure()
            .WithImage(TestImages.Nats)
            .WithReuse(true)
            .WithLabel(ReuseLabel.Key, ReuseLabel.For(this))
            .WithWaitStrategy(
                Wait.ForUnixContainer()
                    .UntilMessageIsLogged("Server is ready")
                    .UntilExternalTcpPortIsAvailable(NatsBuilder.NatsClientPort)
            );
    }
}
