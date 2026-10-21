// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.EntityFramework;
using Headless.Messaging;
using Headless.Messaging.RabbitMq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.RabbitMq;

namespace Tests;

/// <summary>The EF Core outbox bridge relaying through RabbitMQ, with the outbox on the application's PostgreSQL.</summary>
[Collection<OutboxBridgeTestFixture>]
public sealed class OutboxBridgeRabbitMqRelayTests(
    OutboxBridgeTestFixture fixture,
    OutboxBridgeRabbitMqFixture rabbitMq
) : EntityFrameworkOutboxRelayTestsBase, IClassFixture<OutboxBridgeRabbitMqFixture>
{
    protected override void ConfigureTransport(MessagingSetupBuilder setup)
    {
        setup.UseRabbitMq(options =>
        {
            options.HostName = rabbitMq.Container.Hostname;
            options.Port = rabbitMq.Container.GetMappedPublicPort(RabbitMqBuilder.RabbitMqPort);
            options.UserName = RabbitMqBuilder.DefaultUsername;
            options.Password = RabbitMqBuilder.DefaultPassword;
        });
    }

    protected override void ConfigureStorage(MessagingSetupBuilder setup)
    {
        setup.UsePostgreSql(fixture.ConnectionString);
    }

    protected override void UseDatabase(DbContextOptionsBuilder options)
    {
        options.UseNpgsql(fixture.ConnectionString);
    }

    protected override void AddOutboxBridge(IServiceCollection services)
    {
        services.AddHeadlessDbContextServices().AddDomainEvents().AddIntegrationEventOutbox();
    }
}
