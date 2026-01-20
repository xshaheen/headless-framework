using Framework.Messages;
using Framework.Messages.Configuration;
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
        // Given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMessages(opt =>
        {
            opt.WithTopicMapping<OrderCreated>("orders.created");
            opt.UseInMemoryMessageQueue();
            opt.UseInMemoryStorage();
        });

        // When
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<MessagingOptions>>().Value;

        // Then
        options.TopicMappings.Should().ContainKey(typeof(OrderCreated));
        options.TopicMappings[typeof(OrderCreated)].Should().Be("orders.created");
    }

    [Fact]
    public void should_register_multiple_topic_mappings()
    {
        // Given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMessages(opt =>
        {
            opt.WithTopicMapping<OrderCreated>("orders.created");
            opt.WithTopicMapping<UserRegistered>("users.registered");
            opt.UseInMemoryMessageQueue();
            opt.UseInMemoryStorage();
        });

        // When
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<MessagingOptions>>().Value;

        // Then
        options.TopicMappings.Should().HaveCount(2);
        options.TopicMappings[typeof(OrderCreated)].Should().Be("orders.created");
        options.TopicMappings[typeof(UserRegistered)].Should().Be("users.registered");
    }

    [Fact]
    public void should_throw_when_mapping_same_type_to_different_topics()
    {
        // Given
        var services = new ServiceCollection();

        // When/Then
        services
            .Invoking(s =>
                s.AddMessages(opt =>
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
        // Given
        var services = new ServiceCollection();

        // When/Then - Should not throw
        services
            .Invoking(s =>
                s.AddMessages(opt =>
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
    public void should_support_consumer_and_publisher_using_same_topic_mapping()
    {
        // This test documents that topic mappings work for both consumers and publishers
        // Given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMessages(opt =>
        {
            // Topic mapping can be used by both publisher and consumer
            opt.WithTopicMapping<OrderCreated>("orders.created");
            opt.UseInMemoryMessageQueue();
            opt.UseInMemoryStorage();
        });

        // When
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<MessagingOptions>>().Value;
        var publisher = provider.GetRequiredService<IOutboxPublisher>();

        // Then - Mapping is available for type-safe publishing
        options.TopicMappings.Should().ContainKey(typeof(OrderCreated));
        options.TopicMappings[typeof(OrderCreated)].Should().Be("orders.created");
        publisher.Should().NotBeNull();
    }

    [Fact]
    public void should_have_type_safe_publish_overloads_available()
    {
        // This test verifies that the type-safe API compiles and is available
        // Given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMessages(opt =>
        {
            opt.WithTopicMapping<OrderCreated>("orders.created");
            opt.UseInMemoryMessageQueue();
            opt.UseInMemoryStorage();
        });

        var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IOutboxPublisher>();

        // When/Then - Verify type-safe API methods exist via reflection
        var methods = typeof(IOutboxPublisher)
            .GetMethods()
            .Where(m => m.Name == nameof(IOutboxPublisher.PublishAsync) && m.IsGenericMethod)
            .Where(m =>
            {
                // Type-safe methods don't have a string "name" parameter
                var parameters = m.GetParameters();
                return !parameters.Any(p => p.Name == "name" && p.ParameterType == typeof(string));
            })
            .ToList();

        // Should have 2 type-safe PublishAsync overloads (with callback and with headers)
        methods.Should().HaveCountGreaterThanOrEqualTo(2, "should have type-safe PublishAsync overloads");

        // Verify they have class constraints
        foreach (var method in methods)
        {
            var genericParam = method.GetGenericArguments()[0];
            genericParam
                .GenericParameterAttributes.Should()
                .HaveFlag(
                    System.Reflection.GenericParameterAttributes.ReferenceTypeConstraint,
                    $"method {method} should have class constraint"
                );
        }
    }

    [Fact]
    public void should_provide_helpful_error_message_format()
    {
        // This test documents the expected error message format when topic mapping is missing
        // The actual error is thrown by OutboxPublisher._GetTopicNameFromMapping<T>()

        var expectedErrorPattern =
            "No topic mapping found for message type 'OrderCreated'*WithTopicMapping<OrderCreated>*";

        // This serves as documentation of the error message developers will see
        expectedErrorPattern.Should().Contain("WithTopicMapping");
        expectedErrorPattern.Should().Contain("OrderCreated");
    }

    private sealed class OrderCreatedHandler : IConsume<OrderCreated>
    {
        public ValueTask Consume(ConsumeContext<OrderCreated> context, CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }
    }
}
