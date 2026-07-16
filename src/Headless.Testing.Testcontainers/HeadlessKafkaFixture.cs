// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Testcontainers.Kafka;
using Xunit;

namespace Headless.Testing.Testcontainers;

/// <summary>Kafka container fixture pinned to <see cref="TestImages.Kafka"/>.</summary>
/// <remarks>
/// Container reuse is intentionally disabled because the single-node Kafka image does not survive a warm
/// reattach reliably. Its log-based readiness check can match startup output from the previous run before the
/// restarted broker accepts connections, leaving clients connected to a stopped or partially started broker.
/// </remarks>
[PublicAPI]
public class HeadlessKafkaFixture : IAsyncLifetime
{
    private readonly KafkaContainer _container;

    public HeadlessKafkaFixture()
    {
        _container = new KafkaBuilder(TestImages.Kafka).Build();
    }

    /// <summary>Gets the Kafka bootstrap server address.</summary>
    public string ConnectionString => _container.GetBootstrapAddress();

    /// <summary>Starts the Kafka container.</summary>
    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync().ConfigureAwait(false);
    }

    /// <summary>Disposes the Kafka container.</summary>
    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
}
