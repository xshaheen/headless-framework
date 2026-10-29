// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Messaging;
using Headless.Messaging.Kafka;
using Headless.UnitOfWork;
using Npgsql;

namespace Tests;

[Collection("KafkaPostgreSql")]
public sealed class KafkaPostgreSqlMessagingIntegrationTests(KafkaPostgreSqlFixture fixture)
    : MessagingIntegrationTestsBase
{
    private readonly string _prefix = $"stack-{Guid.NewGuid():N}"[..18];

    public override async ValueTask InitializeAsync()
    {
        await fixture.ResetAsync();
        await base.InitializeAsync();
        await EnsureTestSubscriberReadyAsync(timeout: TimeSpan.FromSeconds(60));
    }

    // Kafka offers only the Queue lane, so the suite's Bus-lane scenarios, callbacks among them, do not apply here; the
    // end-to-end scenarios run on the Queue lane instead.
    protected override MessageLane Lane => MessageLane.Queue;

    protected override void ConfigureTransport(MessagingSetupBuilder setup)
    {
        setup.UseKafka(kafka =>
        {
            kafka.Servers = fixture.KafkaConnectionString;
            kafka.TopicOptions.NumPartitions = 1;
            kafka.TopicOptions.ReplicationFactor = 1;
            kafka.MainConfig["allow.auto.create.topics"] = "true";
            kafka.MainConfig["auto.commit.interval.ms"] = "100";
        });
    }

    protected override void ConfigureStorage(MessagingSetupBuilder setup)
    {
        setup.UsePostgreSql(fixture.StorageConnectionString);
    }

    protected override void ConfigureMessaging(MessagingSetupBuilder setup)
    {
        setup.Options.MessageNamePrefix = _prefix;
        setup.Options.RetryProcessor.BaseInterval = TimeSpan.FromSeconds(1);
    }

    protected override DbConnection CreateStorageConnection() => new NpgsqlConnection(fixture.StorageConnectionString);

    protected override ValueTask<IUnitOfWork> BeginUnitOfWorkAsync(
        IUnitOfWorkFactory factory,
        DbConnection connection,
        CancellationToken cancellationToken
    ) => factory.BeginAsync((NpgsqlConnection)connection, cancellationToken);

    protected override Task InterruptBrokerAsync(CancellationToken cancellationToken) =>
        fixture.PauseBrokerAsync(cancellationToken);

    protected override Task RestoreBrokerAsync(CancellationToken cancellationToken) =>
        fixture.UnpauseBrokerAsync(cancellationToken);

    [Fact]
    public override Task should_discover_consumers_from_di()
    {
        return base.should_discover_consumers_from_di();
    }

    [Fact]
    public override Task should_bootstrap_messaging_system()
    {
        return base.should_bootstrap_messaging_system();
    }
}
