// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Testcontainers.Kafka;
using Testcontainers.Xunit;
using Xunit;

namespace Headless.Testing.Testcontainers;

/// <summary>Kafka container fixture pinned to <see cref="TestImages.Kafka"/>.</summary>
/// <remarks>
/// The fixture checks that the bootstrap port answers with the cluster id the broker stored inside the container. Kafka
/// writes the published port into its advertised listeners once, so a restart cannot move it to another port: when
/// another listener holds the port on the host, the start fails and names the port instead of letting clients reach
/// another broker.
/// </remarks>
[PublicAPI]
public class HeadlessKafkaFixture() : ContainerFixture<KafkaBuilder, KafkaContainer>(TestContextMessageSink.Instance)
{
    /// <summary>
    /// Gets the image the fixture runs, <see cref="TestImages.Kafka"/> unless overridden. Override it to run another
    /// version or a variant of the same server.
    /// </summary>
    protected virtual string Image => TestImages.Kafka;

    /// <summary>Gets the Kafka bootstrap server address.</summary>
    public string ConnectionString => Container.GetBootstrapAddress();

    protected override KafkaBuilder Configure()
    {
        return base.Configure().WithImage(Image);
    }

    /// <inheritdoc />
    protected override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync().ConfigureAwait(false);
        await new PublishedPortCheck(KafkaBuilder.KafkaPort, PublishedPortProbes.KafkaClusterId)
            .VerifyAsync(Container, allowRestart: false, TestContext.Current.CancellationToken)
            .ConfigureAwait(false);
    }
}
