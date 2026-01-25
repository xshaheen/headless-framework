// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Demo.Contracts.DomainEvents;
using Headless.Messaging.AzureServiceBus;

namespace Tests;

public sealed class AzureServiceBusOptionsTests
{
    [Fact]
    public void should_have_default_topic_path()
    {
        // given, when
        var options = new AzureServiceBusOptions();

        // then
        options.TopicPath.Should().Be(AzureServiceBusOptions.DefaultTopicPath);
        options.TopicPath.Should().Be("messaging");
    }

    [Fact]
    public void should_have_default_subscription_auto_delete_on_idle()
    {
        // given, when
        var options = new AzureServiceBusOptions();

        // then
        options.SubscriptionAutoDeleteOnIdle.Should().Be(TimeSpan.MaxValue);
    }

    [Fact]
    public void should_have_default_subscription_message_lock_duration()
    {
        // given, when
        var options = new AzureServiceBusOptions();

        // then
        options.SubscriptionMessageLockDuration.Should().Be(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void should_have_default_subscription_default_message_time_to_live()
    {
        // given, when
        var options = new AzureServiceBusOptions();

        // then
        options.SubscriptionDefaultMessageTimeToLive.Should().Be(TimeSpan.MaxValue);
    }

    [Fact]
    public void should_have_default_subscription_max_delivery_count()
    {
        // given, when
        var options = new AzureServiceBusOptions();

        // then
        options.SubscriptionMaxDeliveryCount.Should().Be(10);
    }

    [Fact]
    public void should_have_default_auto_complete_messages_disabled()
    {
        // given, when
        var options = new AzureServiceBusOptions();

        // then
        options.AutoCompleteMessages.Should().BeFalse();
    }

    [Fact]
    public void should_have_default_max_concurrent_calls()
    {
        // given, when
        var options = new AzureServiceBusOptions();

        // then
        options.MaxConcurrentCalls.Should().Be(1);
    }

    [Fact]
    public void should_have_default_max_concurrent_sessions()
    {
        // given, when
        var options = new AzureServiceBusOptions();

        // then
        options.MaxConcurrentSessions.Should().Be(8);
    }

    [Fact]
    public void should_have_default_max_auto_lock_renewal_duration()
    {
        // given, when
        var options = new AzureServiceBusOptions();

        // then
        options.MaxAutoLockRenewalDuration.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void should_have_sessions_disabled_by_default()
    {
        // given, when
        var options = new AzureServiceBusOptions();

        // then
        options.EnableSessions.Should().BeFalse();
    }

    [Fact]
    public void should_have_null_token_credential_by_default()
    {
        // given, when
        var options = new AzureServiceBusOptions();

        // then
        options.TokenCredential.Should().BeNull();
    }

    [Fact]
    public void should_have_empty_default_correlation_headers()
    {
        // given, when
        var options = new AzureServiceBusOptions();

        // then
        options.DefaultCorrelationHeaders.Should().BeEmpty();
    }

    [Fact]
    public void should_have_null_custom_headers_builder_by_default()
    {
        // given, when
        var options = new AzureServiceBusOptions();

        // then
        options.CustomHeadersBuilder.Should().BeNull();
    }

    [Fact]
    public void should_have_null_sql_filters_by_default()
    {
        // given, when
        var options = new AzureServiceBusOptions();

        // then
        options.SqlFilters.Should().BeNull();
    }

    [Fact]
    public void should_configure_custom_producer()
    {
        // given
        var options = new AzureServiceBusOptions();

        // when
        options.ConfigureCustomProducer<EntityCreated>(cfg => cfg.UseTopic("entity-created"));

        // then
        options.CustomProducers.Should().HaveCount(1);
        var producer = options.CustomProducers.Single();
        producer.TopicPath.Should().Be("entity-created");
        producer.MessageTypeName.Should().Be(nameof(EntityCreated));
    }

    [Fact]
    public void should_configure_custom_producer_with_subscription()
    {
        // given
        var options = new AzureServiceBusOptions();

        // when
        options.ConfigureCustomProducer<EntityCreated>(cfg => cfg.UseTopic("entity-created").WithSubscription());

        // then
        var producer = options.CustomProducers.Single();
        producer.CreateSubscription.Should().BeTrue();
    }

    [Fact]
    public void should_configure_multiple_custom_producers()
    {
        // given
        var options = new AzureServiceBusOptions();

        // when
        options
            .ConfigureCustomProducer<EntityCreated>(cfg => cfg.UseTopic("entity-created"))
            .ConfigureCustomProducer<EntityDeleted>(cfg => cfg.UseTopic("entity-deleted"));

        // then
        options.CustomProducers.Should().HaveCount(2);
        options.CustomProducers.Should().Contain(p => p.MessageTypeName == nameof(EntityCreated));
        options.CustomProducers.Should().Contain(p => p.MessageTypeName == nameof(EntityDeleted));
    }

    [Fact]
    public void should_allow_setting_connection_string()
    {
        // given
        var options = new AzureServiceBusOptions();
        const string connectionString =
            "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=abc123";

        // when
        options.ConnectionString = connectionString;

        // then
        options.ConnectionString.Should().Be(connectionString);
    }

    [Fact]
    public void should_allow_setting_namespace()
    {
        // given
        var options = new AzureServiceBusOptions();
        const string @namespace = "test.servicebus.windows.net";

        // when
        options.Namespace = @namespace;

        // then
        options.Namespace.Should().Be(@namespace);
    }

    [Fact]
    public void should_allow_adding_correlation_headers()
    {
        // given
        var options = new AzureServiceBusOptions();

        // when
        options.DefaultCorrelationHeaders.Add("tenant-id", "123");
        options.DefaultCorrelationHeaders.Add("region", "us-east");

        // then
        options.DefaultCorrelationHeaders.Should().HaveCount(2);
        options.DefaultCorrelationHeaders.Should().ContainKey("tenant-id");
        options.DefaultCorrelationHeaders["tenant-id"].Should().Be("123");
    }

    [Fact]
    public void should_allow_setting_sql_filters()
    {
        // given, when
        var options = new AzureServiceBusOptions
        {
            SqlFilters =
            [
                new KeyValuePair<string, string>("priority-filter", "priority > 5"),
                new KeyValuePair<string, string>("type-filter", "type = 'order'"),
            ],
        };

        // then
        options.SqlFilters.Should().HaveCount(2);
        options.SqlFilters.Should().Contain(kv => kv.Key == "priority-filter" && kv.Value == "priority > 5");
    }

    [Fact]
    public void should_allow_setting_session_idle_timeout()
    {
        // given
        var options = new AzureServiceBusOptions();
        var timeout = TimeSpan.FromMinutes(10);

        // when
        options.SessionIdleTimeout = timeout;

        // then
        options.SessionIdleTimeout.Should().Be(timeout);
    }
}
