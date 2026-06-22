// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Testcontainers.RabbitMq;
using Testcontainers.Xunit;

namespace Headless.Testing.Testcontainers;

/// <summary>
/// RabbitMQ container fixture pinned to <see cref="TestImages.RabbitMq"/>.
/// Subclass to add per-project connection helpers, queues, or exchanges.
/// </summary>
/// <remarks>
/// Container reuse is intentionally disabled for RabbitMQ: queue and exchange state left
/// over from a previous test run can cause false failures in isolation-sensitive tests.
/// </remarks>
[PublicAPI]
public class HeadlessRabbitMqFixture()
    : ContainerFixture<RabbitMqBuilder, RabbitMqContainer>(TestContextMessageSink.Instance)
{
    protected override RabbitMqBuilder Configure()
    {
        return base.Configure().WithImage(TestImages.RabbitMq);
    }
}
