// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Nats;
using Headless.Messaging.PostgreSql;
using Microsoft.Extensions.DependencyInjection;
using NATS.Client.JetStream.Models;
using Tests.Helpers;

namespace Tests;

[Collection("NatsPostgreSql")]
public sealed class NatsPostgreSqlMessagingIntegrationTests(NatsPostgreSqlFixture fixture)
    : MessagingIntegrationTestsBase
{
    private readonly string _topicPrefix = $"stack-{Guid.NewGuid():N}"[..18];

    public override async ValueTask InitializeAsync()
    {
        await fixture.ResetAsync();
        await fixture.EnsureStreamAsync(_topicPrefix, $"{_topicPrefix}.>");
        await base.InitializeAsync();
        await EnsureTestSubscriberReadyAsync();
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
    public override Task should_publish_and_consume_message_end_to_end() =>
        base.should_publish_and_consume_message_end_to_end();

    [Fact]
    public override Task should_discover_consumers_from_di() => base.should_discover_consumers_from_di();

    [Fact]
    public override Task should_invoke_consumer_handler_on_message() =>
        base.should_invoke_consumer_handler_on_message();

    [Fact]
    public override Task should_store_received_message_in_storage() => base.should_store_received_message_in_storage();

    [Fact]
    public override Task should_handle_consumer_exception() => base.should_handle_consumer_exception();

    [Fact]
    public override Task should_process_multiple_messages_concurrently() =>
        base.should_process_multiple_messages_concurrently();

    [Fact]
    public override Task should_retry_failed_message() => base.should_retry_failed_message();

    [Fact]
    public override Task should_complete_message_lifecycle() => base.should_complete_message_lifecycle();

    [Fact]
    public override Task should_publish_message_with_headers() => base.should_publish_message_with_headers();

    [Fact]
    public override Task should_publish_delayed_message() => base.should_publish_delayed_message();

    [Fact]
    public override Task should_bootstrap_messaging_system() => base.should_bootstrap_messaging_system();

    [Fact]
    public async Task should_deliver_direct_publish_without_creating_outbox_record()
    {
        var subscriber = ServiceProvider.GetRequiredService<TestSubscriber>();
        subscriber.Clear();

        var monitoringApi = DataStorage.GetMonitoringApi();
        var publishedBefore = await monitoringApi.PublishedSucceededCount(AbortToken);
        var receivedBefore = await monitoringApi.ReceivedSucceededCount(AbortToken);

        var message = new Fixtures.TestMessage
        {
            Id = Guid.NewGuid().ToString(),
            Name = "DirectPublishTest",
            Payload = "direct-path",
        };

        var directPublisher = ServiceProvider.GetRequiredService<IDirectPublisher>();

        await directPublisher.PublishAsync(message, new PublishOptions { Topic = "test-message" }, AbortToken);

        var received = await subscriber.WaitForMessageAsync(TimeSpan.FromSeconds(10), AbortToken);
        received.Should().BeTrue("direct publisher should still deliver through the NATS transport");

        await Task.Delay(TimeSpan.FromSeconds(2), AbortToken);

        var publishedAfter = await monitoringApi.PublishedSucceededCount(AbortToken);
        var receivedAfter = await monitoringApi.ReceivedSucceededCount(AbortToken);

        publishedAfter.Should().Be(publishedBefore, "direct publish bypasses durable outbox persistence");
        receivedAfter.Should().BeGreaterThan(receivedBefore, "the consumer side should still persist received records");
    }

    [Fact]
    public async Task should_attach_runtime_subscriber_after_bootstrap_and_receive_real_nats_message()
    {
        var runtimeSubscriber = ServiceProvider.GetRequiredService<IRuntimeSubscriber>();
        var publisher = ServiceProvider.GetRequiredService<IOutboxPublisher>();

        await using var handle = await runtimeSubscriber.SubscribeAsync<Fixtures.TestMessage>(
            static (context, services, _) =>
            {
                var tcs = services.GetRequiredService<RuntimeDeliveryProbe>();
                tcs.Delivered.TrySetResult(context);
                return ValueTask.CompletedTask;
            },
            new RuntimeSubscriptionOptions
            {
                Topic = "runtime-message",
                Group = "runtime-subscriber",
                HandlerId = "nats-postgresql-runtime-subscriber",
            },
            AbortToken
        );

        var probe = ServiceProvider.GetRequiredService<RuntimeDeliveryProbe>();
        var message = new Fixtures.TestMessage
        {
            Id = Guid.NewGuid().ToString(),
            Name = "RuntimeMessage",
            Payload = "runtime",
        };

        await publisher.PublishAsync(message, new PublishOptions { Topic = "runtime-message" }, AbortToken);

        var consumed = await probe.Delivered.Task.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);

        handle.IsAttached.Should().BeTrue();
        consumed.Message.Id.Should().Be(message.Id);
        consumed.Topic.Should().Be($"{_topicPrefix}.runtime-message");
    }

    [Fact]
    public async Task should_expose_published_and_received_records_through_postgresql_monitoring()
    {
        var subscriber = ServiceProvider.GetRequiredService<TestSubscriber>();
        subscriber.Clear();
        var topic = ResolveTopicName("test-message");

        var correlationId = Guid.NewGuid().ToString("N");
        var publishMessageId = $"msg-{correlationId}";
        var message = new Fixtures.TestMessage
        {
            Id = correlationId,
            Name = "MonitoringTest",
            Payload = $"payload-{correlationId}",
        };

        await Publisher.PublishAsync(
            message,
            new PublishOptions
            {
                Topic = "test-message",
                MessageId = publishMessageId,
                CorrelationId = correlationId,
            },
            AbortToken
        );

        var received = await subscriber.WaitForMessageAsync(TimeSpan.FromSeconds(10), AbortToken);
        received.Should().BeTrue();

        await Task.Delay(TimeSpan.FromSeconds(2), AbortToken);

        var monitoringApi = DataStorage.GetMonitoringApi();
        var publishedPage = await monitoringApi.GetMessagesAsync(
            new MessageQuery
            {
                MessageType = MessageType.Publish,
                StatusName = "Succeeded",
                CurrentPage = 0,
                PageSize = 50,
            },
            AbortToken
        );

        var receivedPage = await monitoringApi.GetMessagesAsync(
            new MessageQuery
            {
                MessageType = MessageType.Subscribe,
                StatusName = "Succeeded",
                CurrentPage = 0,
                PageSize = 50,
            },
            AbortToken
        );

        publishedPage.Items.Should().Contain(item => item.MessageId == publishMessageId && item.Name == topic);
        receivedPage.Items.Should().Contain(item => item.MessageId == publishMessageId && item.Name == topic);
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<RuntimeDeliveryProbe>();
    }

    private sealed class RuntimeDeliveryProbe
    {
        public TaskCompletionSource<ConsumeContext<Fixtures.TestMessage>> Delivered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
