// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Testcontainers.Kafka;
using Testcontainers.Xunit;

namespace Headless.Testing.Testcontainers;

/// <summary>Kafka container fixture pinned to <see cref="TestImages.Kafka"/>.</summary>
[PublicAPI]
public class HeadlessKafkaFixture() : ContainerFixture<KafkaBuilder, KafkaContainer>(TestContextMessageSink.Instance)
{
    /// <summary>
    /// Gets the image the fixture runs, <see cref="TestImages.Kafka"/> unless overridden. Override it to run another
    /// version or a variant of the same server.
    /// </summary>
    protected virtual string Image => TestImages.Kafka;

    protected override KafkaBuilder Configure()
    {
        return base.Configure().WithImage(Image);
    }

    /// <summary>Gets the Kafka bootstrap server address.</summary>
    public string ConnectionString => Container.GetBootstrapAddress();
}
