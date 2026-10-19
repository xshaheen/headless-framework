// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Testcontainers;

namespace Tests;

/// <summary>The RabbitMQ broker the outbox relay tests send through, one per test class.</summary>
[UsedImplicitly]
public sealed class OutboxBridgeRabbitMqFixture : HeadlessRabbitMqFixture;
