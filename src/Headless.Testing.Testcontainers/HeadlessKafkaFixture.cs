// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Testcontainers.Kafka;
using Testcontainers.Xunit;

namespace Headless.Testing.Testcontainers;

/// <summary>Kafka container fixture pinned to <see cref="TestImages.Kafka"/>.</summary>
/// <remarks>
/// Container reuse is intentionally disabled because the single-node Kafka image does not survive a warm
/// reattach reliably. Its log-based readiness check can match startup output from the previous run before the
/// restarted broker accepts connections, leaving clients connected to a stopped or partially started broker.
/// </remarks>
[PublicAPI]
public class HeadlessKafkaFixture() : ContainerFixture<KafkaBuilder, KafkaContainer>(TestContextMessageSink.Instance)
{
    protected override KafkaBuilder Configure()
    {
        return base.Configure().WithImage(TestImages.Kafka);
    }

    /// <summary>Gets the Kafka bootstrap server address.</summary>
    public string ConnectionString => Container.GetBootstrapAddress();
}
