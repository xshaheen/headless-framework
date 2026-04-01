// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Nats;
using Headless.Messaging.PostgreSql;
using Microsoft.Extensions.DependencyInjection;
using NATS.Client.JetStream.Models;
using Tests.Helpers;

namespace Tests;

[Collection("NatsPostgreSql")]
public sealed class NatsPostgreSqlBootstrapReadinessTests(NatsPostgreSqlFixture fixture) : MessagingIntegrationTestsBase
{
    private readonly string _topicPrefix = $"bootstrap-{Guid.NewGuid():N}"[..18];

    public override async ValueTask InitializeAsync()
    {
        await fixture.ResetAsync();
        await fixture.EnsureStreamAsync(_topicPrefix, $"{_topicPrefix}.>");
        await base.InitializeAsync();
    }

    protected override void ConfigureTransport(MessagingOptions options)
    {
        options.UseNats(nats =>
        {
            nats.Servers = fixture.NatsConnectionString;
            nats.EnableSubscriberClientStreamAndSubjectCreation = true;
            nats.StreamOptions = static config => config.Storage = StreamConfigStorage.Memory;
        });
    }

    protected override void ConfigureStorage(MessagingOptions options)
    {
        options.UsePostgreSql(fixture.PostgreSqlConnectionString);
    }

    protected override void ConfigureMessaging(MessagingOptions options)
    {
        options.TopicNamePrefix = _topicPrefix;
        options.FailedRetryInterval = 1;
    }

    [Fact]
    public async Task should_deliver_message_published_immediately_after_bootstrap_returns()
    {
        var subscriber = ServiceProvider.GetRequiredService<TestSubscriber>();
        subscriber.Clear();

        var message = new Fixtures.TestMessage
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "ImmediateAfterBootstrap",
            Payload = "bootstrap",
        };

        await DirectPublisher.PublishAsync(message, new PublishOptions { Topic = "test-message" }, AbortToken);

        var received = await subscriber.WaitForMessageAsync(TimeSpan.FromSeconds(10), AbortToken);

        received.Should().BeTrue("bootstrap should not report success before transport consumers are ready");
        subscriber.ReceivedMessages.Should().ContainSingle(current => current.Id == message.Id);
    }
}
