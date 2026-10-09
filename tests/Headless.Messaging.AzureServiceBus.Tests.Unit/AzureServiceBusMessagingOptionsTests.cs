// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.AzureServiceBus;

namespace Tests;

public sealed class AzureServiceBusMessagingOptionsTests
{
    [Fact]
    public void should_have_documented_defaults()
    {
        // given, when
        var options = new AzureServiceBusMessagingOptions();

        // then
        options.TopicPath.Should().Be(AzureServiceBusMessagingOptions.DefaultTopicPath).And.Be("messaging");
        options.SubscriptionAutoDeleteOnIdle.Should().Be(TimeSpan.MaxValue);
        options.SubscriptionMessageLockDuration.Should().Be(TimeSpan.FromSeconds(60));
        options.SubscriptionDefaultMessageTimeToLive.Should().Be(TimeSpan.MaxValue);
        options.SubscriptionMaxDeliveryCount.Should().Be(10);
        options.PrefetchCount.Should().Be(0);
        options.ClientOptions.Should().BeNull();
        options.MaxAutoLockRenewalDuration.Should().Be(TimeSpan.FromMinutes(5));
        options.EnableSessions.Should().BeFalse();
        options.TokenCredential.Should().BeNull();
        options.DefaultCorrelationHeaders.Should().BeEmpty();
        options.CustomHeadersBuilder.Should().BeNull();
        options.SqlFilters.Should().BeEmpty();
    }

    [Fact]
    public void should_configure_custom_producer()
    {
        // given
        var options = new AzureServiceBusMessagingOptions();

        // when
        options.ConfigureCustomProducer<EntityCreated>(cfg => cfg.UseTopic("entity-created"));

        // then
        options.CustomProducers.Should().ContainSingle();
        var producer = options.CustomProducers.Single();
        producer.TopicPath.Should().Be("entity-created");
        producer.MessageTypeName.Should().Be(nameof(EntityCreated));
    }

    [Fact]
    public void should_configure_custom_producer_with_subscription()
    {
        // given
        var options = new AzureServiceBusMessagingOptions();

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
        var options = new AzureServiceBusMessagingOptions();

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
    public void should_reject_a_negative_prefetch_count()
    {
        // given
        var options = new AzureServiceBusMessagingOptions
        {
            ConnectionString = "Endpoint=sb://ns.servicebus.windows.net/;SharedAccessKeyName=k;SharedAccessKey=v",
            PrefetchCount = -1,
        };

        // when
        var result = new AzureServiceBusMessagingOptionsValidator().Validate(options);

        // then
        result.IsValid.Should().BeFalse();
        result
            .Errors.Should()
            .ContainSingle(e => e.PropertyName == nameof(AzureServiceBusMessagingOptions.PrefetchCount));
    }
}
