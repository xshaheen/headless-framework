// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Testcontainers.Kafka;
using Testcontainers.Xunit;
using Xunit;

namespace Headless.Testing.Testcontainers;

/// <summary>Kafka container fixture pinned to <see cref="TestImages.Kafka"/>.</summary>
[PublicAPI]
public class HeadlessKafkaFixture() : ContainerFixture<KafkaBuilder, KafkaContainer>(TestContextMessageSink.Instance)
{
    protected override KafkaBuilder Configure()
    {
        return base.Configure().WithImage(TestImages.Kafka);
    }

    /// <summary>Gets the Kafka bootstrap server address.</summary>
    public string ConnectionString => Container.GetBootstrapAddress();

    /// <inheritdoc />
    protected override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync().ConfigureAwait(false);
        await ContainerHostPort
            .EnsureReachableAsync(
                Container,
                KafkaBuilder.KafkaPort,
                cancellationToken: TestContext.Current.CancellationToken
            )
            .ConfigureAwait(false);
    }
}
