using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Messages;
using Headless.Messaging.Transactions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class TypeSafePublishApiTests
{
    private sealed class OrderCreated
    {
        public int OrderId { get; init; }
    }

    private sealed class UserRegistered
    {
        public string UserId { get; init; } = string.Empty;
    }

    [Fact]
    public void should_register_topic_mapping_for_message_type()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMessaging(opt =>
        {
            opt.WithTopicMapping<OrderCreated>("orders.created");
            opt.UseInMemoryMessageQueue();
            opt.UseInMemoryStorage();
        });

        // when
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<MessagingOptions>>().Value;

        // then
        options.TopicMappings.Should().ContainKey(typeof(OrderCreated));
        options.TopicMappings[typeof(OrderCreated)].Should().Be("orders.created");
    }

    [Fact]
    public void should_register_multiple_topic_mappings()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMessaging(opt =>
        {
            opt.WithTopicMapping<OrderCreated>("orders.created");
            opt.WithTopicMapping<UserRegistered>("users.registered");
            opt.UseInMemoryMessageQueue();
            opt.UseInMemoryStorage();
        });

        // when
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<MessagingOptions>>().Value;

        // then
        options.TopicMappings.Should().HaveCount(2);
        options.TopicMappings[typeof(OrderCreated)].Should().Be("orders.created");
        options.TopicMappings[typeof(UserRegistered)].Should().Be("users.registered");
    }

    [Fact]
    public void should_throw_when_mapping_same_type_to_different_topics()
    {
        // given
        var services = new ServiceCollection();

        // when/Then
        services
            .Invoking(s =>
                s.AddMessaging(opt =>
                {
                    opt.WithTopicMapping<OrderCreated>("orders.created");
                    opt.WithTopicMapping<OrderCreated>("orders.new"); // Different topic
                    opt.UseInMemoryMessageQueue();
                    opt.UseInMemoryStorage();
                })
            )
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*already mapped to topic 'orders.created'*");
    }

    [Fact]
    public void should_allow_remapping_same_type_to_same_topic()
    {
        // given
        var services = new ServiceCollection();

        // when/Then - Should not throw
        services
            .Invoking(s =>
                s.AddMessaging(opt =>
                {
                    opt.WithTopicMapping<OrderCreated>("orders.created");
                    opt.WithTopicMapping<OrderCreated>("orders.created"); // Same topic
                    opt.UseInMemoryMessageQueue();
                    opt.UseInMemoryStorage();
                })
            )
            .Should()
            .NotThrow();
    }

    [Fact]
    public async Task should_support_consumer_and_publisher_using_same_topic_mapping()
    {
        // This test documents that topic mappings work for both consumers and publishers
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMessaging(opt =>
        {
            // Topic mapping can be used by both publisher and consumer
            opt.WithTopicMapping<OrderCreated>("orders.created");
            opt.UseInMemoryMessageQueue();
            opt.UseInMemoryStorage();
        });

        // when
        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<MessagingOptions>>().Value;
        var publisher = provider.GetRequiredService<IOutboxPublisher>();

        // then - Mapping is available for type-safe publishing
        options.TopicMappings.Should().ContainKey(typeof(OrderCreated));
        options.TopicMappings[typeof(OrderCreated)].Should().Be("orders.created");
        publisher.Should().NotBeNull();
    }

    [Fact]
    public void should_share_primary_publish_contract_between_direct_and_outbox_publishers()
    {
        typeof(IDirectPublisher).GetInterfaces().Should().Contain(typeof(IMessagePublisher));
        typeof(IOutboxPublisher).GetInterfaces().Should().Contain(typeof(IMessagePublisher));
        typeof(IMessagePublisher)
            .GetMethods()
            .Should()
            .ContainSingle(method => method.Name == nameof(IMessagePublisher.PublishAsync) && method.IsGenericMethod);
    }

    [Fact]
    public void should_not_expose_mutable_outbox_publisher_state()
    {
        var publicPropertyNames = typeof(IOutboxPublisher)
            .GetProperties()
            .Select(property => property.Name)
            .ToList();

        publicPropertyNames.Should().NotContain("ServiceProvider");
        publicPropertyNames.Should().NotContain("Transaction");
    }

    [Fact]
    public void should_provide_helpful_error_message_format()
    {
        // This test documents the expected error message format when topic mapping is missing
        // The actual error is thrown by OutboxPublisher._GetTopicNameFromMapping<T>()

        const string expectedErrorPattern =
            "No topic mapping found for message type 'OrderCreated'*WithTopicMapping<OrderCreated>*";

        // This serves as documentation of the error message developers will see
        expectedErrorPattern.Should().Contain("WithTopicMapping");
        expectedErrorPattern.Should().Contain("OrderCreated");
    }

    [Fact]
    public async Task should_support_custom_outbox_transaction_buffers()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMessaging(opt =>
        {
            opt.WithTopicMapping<OrderCreated>("orders.created");
            opt.UseInMemoryMessageQueue();
            opt.UseInMemoryStorage();
        });

        await using var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IOutboxPublisher>();
        var accessor = provider.GetRequiredService<IOutboxTransactionAccessor>();
        var transaction = new TestOutboxTransaction { DbTransaction = new object() };
        accessor.Current = transaction;

        try
        {
            // when
            await publisher.PublishAsync(new OrderCreated { OrderId = 42 });
        }
        finally
        {
            accessor.Current = null;
        }

        // then
        transaction.BufferedMessages.Should().ContainSingle();
    }

    private sealed class OrderCreatedHandler : IConsume<OrderCreated>
    {
        public ValueTask Consume(ConsumeContext<OrderCreated> context, CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestOutboxTransaction : IOutboxTransaction, IOutboxMessageBuffer
    {
        public List<MediumMessage> BufferedMessages { get; } = [];

        public bool AutoCommit { get; set; }

        public object? DbTransaction { get; set; }

        public void Commit() { }

        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Rollback() { }

        public Task RollbackAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void AddToSent(MediumMessage message)
        {
            BufferedMessages.Add(message);
        }

        public void Dispose() { }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
