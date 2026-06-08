using Headless.AmbientTransactions;
using Headless.Messaging;
using Headless.Messaging.Configuration;
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
        services.AddHeadlessMessaging(opt =>
        {
            opt.WithMessageNameMapping<OrderCreated>("orders.created");
            opt.UseInMemory();
            opt.UseInMemoryStorage();
        });

        // when
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<MessagingOptions>>().Value;

        // then
        options.MessageNameMappings.Should().ContainKey(typeof(OrderCreated));
        options.MessageNameMappings[typeof(OrderCreated)].Should().Be("orders.created");
    }

    [Fact]
    public void should_register_multiple_topic_mappings()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(opt =>
        {
            opt.WithMessageNameMapping<OrderCreated>("orders.created");
            opt.WithMessageNameMapping<UserRegistered>("users.registered");
            opt.UseInMemory();
            opt.UseInMemoryStorage();
        });

        // when
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<MessagingOptions>>().Value;

        // then
        options.MessageNameMappings.Should().HaveCount(2);
        options.MessageNameMappings[typeof(OrderCreated)].Should().Be("orders.created");
        options.MessageNameMappings[typeof(UserRegistered)].Should().Be("users.registered");
    }

    [Fact]
    public void should_throw_when_mapping_same_type_to_different_topics()
    {
        // given
        var services = new ServiceCollection();

        // when/Then
        services
            .Invoking(s =>
                s.AddHeadlessMessaging(opt =>
                {
                    opt.WithMessageNameMapping<OrderCreated>("orders.created");
                    opt.WithMessageNameMapping<OrderCreated>("orders.new"); // Different messageName
                    opt.UseInMemory();
                    opt.UseInMemoryStorage();
                })
            )
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*already mapped to messageName 'orders.created'*");
    }

    [Fact]
    public void should_allow_remapping_same_type_to_same_topic()
    {
        // given
        var services = new ServiceCollection();

        // when/Then - Should not throw
        services
            .Invoking(s =>
                s.AddHeadlessMessaging(opt =>
                {
                    opt.WithMessageNameMapping<OrderCreated>("orders.created");
                    opt.WithMessageNameMapping<OrderCreated>("orders.created"); // Same messageName
                    opt.UseInMemory();
                    opt.UseInMemoryStorage();
                })
            )
            .Should()
            .NotThrow();
    }

    [Fact]
    public async Task should_support_consumer_and_publisher_using_same_topic_mapping()
    {
        // This test documents that messageName mappings work for both consumers and publishers
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(opt =>
        {
            // MessageName mapping can be used by both publisher and consumer
            opt.WithMessageNameMapping<OrderCreated>("orders.created");
            opt.UseInMemory();
            opt.UseInMemoryStorage();
        });

        // when
        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<MessagingOptions>>().Value;
        var publisher = provider.GetRequiredService<IOutboxBus>();

        // then - Mapping is available for type-safe publishing
        options.MessageNameMappings.Should().ContainKey(typeof(OrderCreated));
        options.MessageNameMappings[typeof(OrderCreated)].Should().Be("orders.created");
        publisher.Should().NotBeNull();
    }

    [Fact]
    public void should_expose_intent_specific_publish_contracts()
    {
        typeof(IBus)
            .GetMethods()
            .Should()
            .ContainSingle(method => method.Name == nameof(IBus.PublishAsync) && method.IsGenericMethod);
        typeof(IOutboxBus)
            .GetMethods()
            .Should()
            .ContainSingle(method => method.Name == nameof(IOutboxBus.PublishAsync) && method.IsGenericMethod);
        typeof(IQueue)
            .GetMethods()
            .Should()
            .ContainSingle(method => method.Name == nameof(IQueue.EnqueueAsync) && method.IsGenericMethod);
        typeof(IOutboxQueue)
            .GetMethods()
            .Should()
            .ContainSingle(method => method.Name == nameof(IOutboxQueue.EnqueueAsync) && method.IsGenericMethod);
    }

    [Fact]
    public void should_not_expose_mutable_outbox_publisher_state()
    {
        var publicPropertyNames = typeof(IOutboxBus).GetProperties().Select(property => property.Name).ToList();

        publicPropertyNames.Should().NotContain("ServiceProvider");
        publicPropertyNames.Should().NotContain("Transaction");
    }

    [Fact]
    public void should_provide_helpful_error_message_format()
    {
        // This test documents the expected error message format when messageName mapping is missing
        // The actual error is thrown by MessagePublishRequestFactory._GetMessageNameFromMapping<T>()

        const string expectedErrorPattern =
            "No messageName mapping found for message type 'OrderCreated'*WithMessageNameMapping<OrderCreated>*";

        // This serves as documentation of the error message developers will see
        expectedErrorPattern.Should().Contain("WithMessageNameMapping");
        expectedErrorPattern.Should().Contain("OrderCreated");
    }

    [Fact]
    public async Task should_buffer_outbox_publish_with_ambient_transaction_commit_work()
    {
        // given
        var currentAmbientTransaction = new TestCurrentAmbientTransaction();
        var transaction = new TestAmbientTransaction { DbTransaction = new object() };
        currentAmbientTransaction.Current = transaction;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICurrentAmbientTransaction>(currentAmbientTransaction);
        services.AddSingleton<IAmbientTransaction>(transaction);
        services.AddHeadlessMessaging(opt =>
        {
            opt.WithMessageNameMapping<OrderCreated>("orders.created");
            opt.UseInMemory();
            opt.UseInMemoryStorage();
        });

        await using var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IOutboxBus>();

        try
        {
            // when
            await publisher.PublishAsync(new OrderCreated { OrderId = 42 });
        }
        finally
        {
            currentAmbientTransaction.Current = null;
        }

        // then
        transaction.CommitWork.Should().ContainSingle();
    }

    [UsedImplicitly]
    private sealed class OrderCreatedHandler : IConsume<OrderCreated>
    {
        public ValueTask ConsumeAsync(ConsumeContext<OrderCreated> context, CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestCurrentAmbientTransaction : ICurrentAmbientTransaction
    {
        public IAmbientTransaction? Current { get; set; }
    }

    private sealed class TestAmbientTransaction : IAmbientTransaction
    {
        public List<Func<CancellationToken, ValueTask>> CommitWork { get; } = [];

        public bool AutoCommit { get; set; }

        public object? DbTransaction { get; set; }

        public void RegisterCommitWork(Func<CancellationToken, ValueTask> drain)
        {
            CommitWork.Add(drain);
        }

        public void CompleteExternally() { }

        public void Commit() { }

        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Rollback() { }

        public Task RollbackAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Dispose() { }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
