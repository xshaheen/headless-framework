// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Messaging;
using Headless.Messaging.RabbitMq;
using Headless.UnitOfWork;
using Microsoft.Data.SqlClient;
using Testcontainers.RabbitMq;

namespace Tests;

[Collection("RabbitMqSqlServer")]
public sealed class RabbitMqSqlServerMessagingIntegrationTests(RabbitMqSqlServerFixture fixture)
    : MessagingIntegrationTestsBase
{
    private readonly string _prefix = $"stack-{Guid.NewGuid():N}"[..18];

    public override async ValueTask InitializeAsync()
    {
        await fixture.ResetAsync();
        await base.InitializeAsync();
        await EnsureTestSubscriberReadyAsync();
    }

    protected override void ConfigureTransport(MessagingSetupBuilder setup)
    {
        setup.UseRabbitMq(rabbitMq =>
        {
            rabbitMq.HostName = fixture.RabbitMqHostName;
            rabbitMq.Port = fixture.RabbitMqPort;
            rabbitMq.UserName = RabbitMqBuilder.DefaultUsername;
            rabbitMq.Password = RabbitMqBuilder.DefaultPassword;
            rabbitMq.ExchangeName = _prefix;
        });
    }

    protected override void ConfigureStorage(MessagingSetupBuilder setup)
    {
        setup.UseSqlServer(fixture.StorageConnectionString);
    }

    protected override void ConfigureMessaging(MessagingSetupBuilder setup)
    {
        setup.Options.MessageNamePrefix = _prefix;
        setup.Options.RetryProcessor.BaseInterval = TimeSpan.FromSeconds(1);
    }

    protected override DbConnection CreateStorageConnection() => new SqlConnection(fixture.StorageConnectionString);

    protected override ValueTask<IUnitOfWork> BeginUnitOfWorkAsync(
        IUnitOfWorkFactory factory,
        DbConnection connection,
        CancellationToken cancellationToken
    ) => factory.BeginAsync((SqlConnection)connection, cancellationToken);

    protected override Task InterruptBrokerAsync(CancellationToken cancellationToken) =>
        fixture.PauseBrokerAsync(cancellationToken);

    protected override Task RestoreBrokerAsync(CancellationToken cancellationToken) =>
        fixture.UnpauseBrokerAsync(cancellationToken);

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
}
