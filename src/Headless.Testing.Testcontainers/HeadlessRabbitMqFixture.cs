// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Testcontainers.RabbitMq;
using Testcontainers.Xunit;

namespace Headless.Testing.Testcontainers;

/// <summary>
/// RabbitMQ container fixture pinned to <see cref="TestImages.RabbitMq"/>.
/// Subclass to add per-project connection helpers, queues, or exchanges.
/// </summary>
/// <remarks>
/// Container reuse is intentionally disabled for RabbitMQ. Unlike the other backends it does not survive a
/// warm reattach: when a reused broker is restarted, its log-based readiness wait matches the previous run's
/// startup log lines and reports ready before AMQP is actually accepting, so tests fail with
/// <c>BrokerUnreachableException</c> (validated: 16 failures on the second run). Making reuse safe here would
/// need an AMQP-level readiness wait rather than a log match.
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
