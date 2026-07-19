// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
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

    protected override void ConfigureTransport(MessagingSetupBuilder setup)
    {
        setup.UseNats(nats =>
        {
            nats.Servers = fixture.NatsConnectionString;
            nats.EnableSubscriberClientStreamAndSubjectCreation = true;
            nats.StreamOptions = static config => config.Storage = StreamConfigStorage.Memory;
        });
    }

    protected override void ConfigureStorage(MessagingSetupBuilder setup)
    {
        setup.UsePostgreSql(fixture.PostgreSqlConnectionString);
    }

    protected override void ConfigureMessaging(MessagingSetupBuilder setup)
    {
        setup.Options.MessageNamePrefix = _topicPrefix;
        setup.Options.RetryProcessor.BaseInterval = TimeSpan.FromSeconds(1);
    }

    [Fact]
    public override Task should_publish_and_consume_message_end_to_end()
    {
        return base.should_publish_and_consume_message_end_to_end();
    }

    [Fact]
    public override Task should_discover_consumers_from_di()
    {
        return base.should_discover_consumers_from_di();
    }

    [Fact]
    public override Task should_invoke_consumer_handler_on_message()
    {
        return base.should_invoke_consumer_handler_on_message();
    }

    [Fact]
    public override Task should_store_received_message_in_storage()
    {
        return base.should_store_received_message_in_storage();
    }

    [Fact]
    public override Task should_handle_consumer_exception()
    {
        return base.should_handle_consumer_exception();
    }

    [Fact]
    public override Task should_process_multiple_messages_concurrently()
    {
        return base.should_process_multiple_messages_concurrently();
    }

    [Fact]
    public override Task should_retry_failed_message()
    {
        return base.should_retry_failed_message();
    }

    [Fact]
    public override Task should_complete_message_lifecycle()
    {
        return base.should_complete_message_lifecycle();
    }

    [Fact]
    public override Task should_publish_message_with_headers()
    {
        return base.should_publish_message_with_headers();
    }

    [Fact]
    public override Task should_publish_delayed_message()
    {
        return base.should_publish_delayed_message();
    }

    [Fact]
    public override Task should_bootstrap_messaging_system()
    {
        return base.should_bootstrap_messaging_system();
    }

    [Fact]
    public override Task should_publish_callback_response_for_bus_request()
    {
        return base.should_publish_callback_response_for_bus_request();
    }

    [Fact]
    public override Task should_publish_callback_response_for_queue_request()
    {
        return base.should_publish_callback_response_for_queue_request();
    }

    [Fact]
    public override Task should_publish_typed_null_callback_response()
    {
        return base.should_publish_typed_null_callback_response();
    }

    [Fact]
    public override Task should_publish_headers_only_callback_response()
    {
        return base.should_publish_headers_only_callback_response();
    }

    [Fact]
    public override Task should_rewrite_callback_when_response_is_set()
    {
        return base.should_rewrite_callback_when_response_is_set();
    }

    [Fact]
    public override Task should_remove_callback_even_when_response_is_set()
    {
        return base.should_remove_callback_even_when_response_is_set();
    }

    [Fact]
    public override Task should_drop_set_response_when_callback_name_is_absent()
    {
        return base.should_drop_set_response_when_callback_name_is_absent();
    }

    [Fact]
    public override Task should_publish_one_callback_response_per_fanout_subscriber()
    {
        return base.should_publish_one_callback_response_per_fanout_subscriber();
    }

    [Fact]
    public override Task should_isolate_callback_controls_between_fanout_subscribers()
    {
        return base.should_isolate_callback_controls_between_fanout_subscribers();
    }

    [Fact]
    public override Task should_chain_callback_when_response_sets_next_callback_header()
    {
        return base.should_chain_callback_when_response_sets_next_callback_header();
    }

    [Fact]
    public override Task should_fail_consume_when_callback_response_cannot_serialize()
    {
        return base.should_fail_consume_when_callback_response_cannot_serialize();
    }

    [Fact]
    public async Task should_deliver_direct_publish_without_creating_outbox_record()
    {
        var subscriber = ServiceProvider.GetRequiredService<TestSubscriber>();
        subscriber.Clear();

        var monitoringApi = DataStorage.GetMonitoringApi();
        var publishedBefore = await monitoringApi.GetPublishedSucceededCountAsync(AbortToken);
        var receivedBefore = await monitoringApi.GetReceivedSucceededCountAsync(AbortToken);

        var message = new Fixtures.TestMessage
        {
            Id = Guid.NewGuid().ToString(),
            Name = "DirectPublishTest",
            Payload = "direct-path",
        };

        var directPublisher = ServiceProvider.GetRequiredService<IBus>();

        await directPublisher.PublishAsync(message, new PublishOptions { MessageName = "test-message" }, AbortToken);

        var received = await subscriber.WaitForMessageAsync(TimeSpan.FromSeconds(10), AbortToken);
        received.Should().BeTrue("bus should still deliver through the NATS transport");

        await Task.Delay(TimeSpan.FromSeconds(2), AbortToken);

        var publishedAfter = await monitoringApi.GetPublishedSucceededCountAsync(AbortToken);
        var receivedAfter = await monitoringApi.GetReceivedSucceededCountAsync(AbortToken);

        publishedAfter.Should().Be(publishedBefore, "direct publish bypasses durable outbox persistence");
        receivedAfter.Should().BeGreaterThan(receivedBefore, "the consumer side should still persist received records");
    }

    [Fact]
    public async Task should_attach_runtime_subscriber_after_bootstrap_and_receive_real_nats_message()
    {
        var runtimeSubscriber = ServiceProvider.GetRequiredService<IRuntimeSubscriber>();
        var publisher = ServiceProvider.GetRequiredService<IOutboxBus>();

        await using var handle = await runtimeSubscriber.SubscribeAsync<Fixtures.TestMessage>(
            static (context, services, _) =>
            {
                var tcs = services.GetRequiredService<RuntimeDeliveryProbe>();
                tcs.Delivered.TrySetResult(context);
                return ValueTask.CompletedTask;
            },
            new RuntimeSubscriptionOptions
            {
                MessageName = "runtime-message",
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

        await publisher.PublishAsync(message, new PublishOptions { MessageName = "runtime-message" }, AbortToken);

        var consumed = await probe.Delivered.Task.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);

        handle.IsAttached.Should().BeTrue();
        consumed.Message.Id.Should().Be(message.Id);
        consumed.MessageName.Should().Be($"{_topicPrefix}.runtime-message");
    }

    [Fact]
    public async Task should_expose_published_and_received_records_through_postgresql_monitoring()
    {
        var subscriber = ServiceProvider.GetRequiredService<TestSubscriber>();
        subscriber.Clear();
        var messageName = ResolveMessageName("test-message");

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
                MessageName = "test-message",
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
                StatusName = StatusName.Succeeded,
                CurrentPage = 0,
                PageSize = 50,
            },
            AbortToken
        );

        var receivedPage = await monitoringApi.GetMessagesAsync(
            new MessageQuery
            {
                MessageType = MessageType.Subscribe,
                StatusName = StatusName.Succeeded,
                CurrentPage = 0,
                PageSize = 50,
            },
            AbortToken
        );

        publishedPage.Items.Should().Contain(item => item.MessageId == publishMessageId && item.Name == messageName);
        receivedPage.Items.Should().Contain(item => item.MessageId == publishMessageId && item.Name == messageName);
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
