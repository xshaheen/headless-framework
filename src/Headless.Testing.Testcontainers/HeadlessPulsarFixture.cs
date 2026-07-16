// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Testcontainers.Pulsar;
using Testcontainers.Xunit;

namespace Headless.Testing.Testcontainers;

/// <summary>Pulsar container fixture pinned to <see cref="TestImages.Pulsar"/>.</summary>
/// <remarks>
/// Container reuse is intentionally disabled so isolated outage tests cannot reattach to or mutate the
/// collection-scoped baseline broker.
/// </remarks>
[PublicAPI]
public class HeadlessPulsarFixture()
    : ContainerFixture<PulsarBuilder, PulsarContainer>(TestContextMessageSink.Instance),
        IAsyncDisposable
{
    protected override PulsarBuilder Configure()
    {
        return base.Configure().WithImage(TestImages.Pulsar);
    }

    /// <summary>Gets the Pulsar binary-protocol service URL.</summary>
    public string ConnectionString => Container.GetBrokerAddress().TrimEnd('/');

    /// <summary>Starts the Pulsar container for fixtures that own an isolated broker lifecycle.</summary>
    public new ValueTask InitializeAsync()
    {
        return base.InitializeAsync();
    }

    /// <summary>Disposes the Pulsar container for fixtures that own an isolated broker lifecycle.</summary>
    public async ValueTask DisposeAsync()
    {
        await Container.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
}
