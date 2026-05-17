// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Testcontainers.RabbitMq;
using Testcontainers.Xunit;

namespace Headless.Testing.Testcontainers;

/// <summary>
/// Shared RabbitMQ container fixture pinned to <see cref="TestImages.RabbitMq"/>.
/// Subclass to add per-project connection helpers, queues, or exchanges.
/// </summary>
[PublicAPI]
public class HeadlessRabbitMqFixture()
    : ContainerFixture<RabbitMqBuilder, RabbitMqContainer>(TestContextMessageSink.Instance)
{
    protected override RabbitMqBuilder Configure()
    {
        return base.Configure().WithImage(TestImages.RabbitMq).WithReuse(true);
    }
}
