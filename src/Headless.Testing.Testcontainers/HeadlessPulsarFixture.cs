// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Testcontainers.Pulsar;
using Xunit;

namespace Headless.Testing.Testcontainers;

/// <summary>Pulsar container fixture pinned to <see cref="TestImages.Pulsar"/>.</summary>
/// <remarks>
/// Container reuse is intentionally disabled so isolated outage tests cannot reattach to or mutate the
/// collection-scoped baseline broker.
/// </remarks>
[PublicAPI]
public class HeadlessPulsarFixture : IAsyncLifetime
{
    public HeadlessPulsarFixture()
    {
        Container = new PulsarBuilder(TestImages.Pulsar).Build();
    }

    /// <summary>Gets the owned Pulsar container for derived test fixtures.</summary>
    protected PulsarContainer Container { get; }

    /// <summary>Gets the Pulsar binary-protocol service URL.</summary>
    public string ConnectionString => Container.GetBrokerAddress().TrimEnd('/');

    /// <summary>Starts the Pulsar container.</summary>
    public async ValueTask InitializeAsync()
    {
        await Container.StartAsync().ConfigureAwait(false);
    }

    /// <summary>Disposes the Pulsar container.</summary>
    public async ValueTask DisposeAsync()
    {
        await Container.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
}
