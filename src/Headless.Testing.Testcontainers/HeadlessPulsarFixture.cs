// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Testcontainers.Pulsar;
using Testcontainers.Xunit;

namespace Headless.Testing.Testcontainers;

/// <summary>Pulsar container fixture pinned to <see cref="TestImages.Pulsar"/>.</summary>
[PublicAPI]
public class HeadlessPulsarFixture() : ContainerFixture<PulsarBuilder, PulsarContainer>(TestContextMessageSink.Instance)
{
    protected override PulsarBuilder Configure()
    {
        return base.Configure().WithImage(TestImages.Pulsar);
    }

    /// <summary>Gets the Pulsar binary-protocol service URL.</summary>
    public string ConnectionString => Container.GetBrokerAddress().TrimEnd('/');
}
