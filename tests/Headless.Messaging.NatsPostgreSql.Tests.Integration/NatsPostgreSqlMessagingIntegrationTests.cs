// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Nats;
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
            nats.StreamProvisioning = NatsStreamProvisioning.Reconcile;
            nats.StreamOptions = static config => config.Storage = StreamConfigStorage.Memory;
        });
    }

    protected override void ConfigureStorage(MessagingSetupBuilder setup)
    {
        setup.Options.MinimumInboxGuarantee = InboxGuarantee.Durable;
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

        var publishMessageId = $"direct-{Guid.NewGuid():N}";
        var message = new Fixtures.TestMessage
        {
            Id = Guid.NewGuid().ToString(),
            Name = "DirectPublishTest",
            Payload = "direct-path",
        };

        // The bus defaults to durable delivery, so the direct path has to be requested explicitly.
        await Publisher.PublishAsync(
            message,
            new PublishOptions
            {
                MessageName = "test-message",
                MessageId = publishMessageId,
                DeliveryMode = DeliveryMode.Direct,
            },
            AbortToken
        );

        var received = await subscriber.WaitForMessageAsync(TimeSpan.FromSeconds(10), AbortToken);
        received.Should().BeTrue("bus should still deliver through the NATS transport");

        // Assert on this message's own rows rather than table-wide counts: the readiness probe publishes durably
        // during setup, and its outbox row may still be completing when the test starts.
        var monitoringApi = DataStorage.GetMonitoringApi();
        var receivedRecord = await _WaitForRecordAsync(monitoringApi, MessageType.Subscribe, publishMessageId);
        var publishedRecords = await _GetRecordsAsync(monitoringApi, MessageType.Publish, publishMessageId);

        receivedRecord.Should().BeTrue("the consumer side should still persist received records");
        publishedRecords.Should().BeEmpty("direct publish bypasses durable outbox persistence");
    }

    private static async Task<bool> _WaitForRecordAsync(
        IMonitoringApi monitoringApi,
        MessageType messageType,
        string messageId,
        StatusName? statusName = null
    )
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);

        while (DateTimeOffset.UtcNow < deadline)
        {
            if ((await _GetRecordsAsync(monitoringApi, messageType, messageId, statusName)).Count > 0)
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), AbortToken);
        }

        return false;
    }

    private static async Task<IReadOnlyList<MessageView>> _GetRecordsAsync(
        IMonitoringApi monitoringApi,
        MessageType messageType,
        string messageId,
        StatusName? statusName = null
    )
    {
        var page = await monitoringApi.GetMessagesAsync(
            new MessageQuery
            {
                MessageType = messageType,
                StatusName = statusName,
                CurrentPage = 0,
                PageSize = 200,
            },
            AbortToken
        );

        return page.Items.Where(item => string.Equals(item.MessageId, messageId, StringComparison.Ordinal)).ToList();
    }

    [Fact]
    public async Task should_attach_runtime_subscriber_after_bootstrap_and_receive_real_nats_message()
    {
        var runtimeSubscriber = ServiceProvider.GetRequiredService<IRuntimeSubscriber>();
        var publisher = Publisher;

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
                Identity = "runtime-subscriber",
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

        await publisher.PublishAsync(
            message,
            new PublishOptions { MessageName = "runtime-message", DeliveryMode = DeliveryMode.Durable },
            AbortToken
        );

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
                DeliveryMode = DeliveryMode.Durable,
            },
            AbortToken
        );

        var received = await subscriber.WaitForMessageAsync(TimeSpan.FromSeconds(10), AbortToken);
        received.Should().BeTrue();

        // The status flips to Succeeded after delivery, so poll for the final state instead of reading once.
        var monitoringApi = DataStorage.GetMonitoringApi();
        (await _WaitForRecordAsync(monitoringApi, MessageType.Publish, publishMessageId, StatusName.Succeeded))
            .Should()
            .BeTrue();
        (await _WaitForRecordAsync(monitoringApi, MessageType.Subscribe, publishMessageId, StatusName.Succeeded))
            .Should()
            .BeTrue();

        var publishedRecords = await _GetRecordsAsync(
            monitoringApi,
            MessageType.Publish,
            publishMessageId,
            StatusName.Succeeded
        );
        var receivedRecords = await _GetRecordsAsync(
            monitoringApi,
            MessageType.Subscribe,
            publishMessageId,
            StatusName.Succeeded
        );

        publishedRecords.Should().Contain(item => item.Name == messageName);
        receivedRecords.Should().Contain(item => item.Name == messageName);
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
