// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Testcontainers.RabbitMq;
using Testcontainers.Xunit;
using Xunit;

namespace Headless.Testing.Testcontainers;

/// <summary>
/// RabbitMQ container fixture pinned to <see cref="TestImages.RabbitMq"/>.
/// Subclass to add per-project connection helpers, queues, or exchanges.
/// </summary>
[PublicAPI]
public class HeadlessRabbitMqFixture()
    : ContainerFixture<RabbitMqBuilder, RabbitMqContainer>(TestContextMessageSink.Instance)
{
    protected override RabbitMqBuilder Configure()
    {
        return base.Configure().WithImage(TestImages.RabbitMq);
    }

    /// <inheritdoc />
    protected override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync().ConfigureAwait(false);
        await ContainerHostPort
            .EnsureReachableAsync(
                Container,
                RabbitMqBuilder.RabbitMqPort,
                cancellationToken: TestContext.Current.CancellationToken
            )
            .ConfigureAwait(false);
    }
}
