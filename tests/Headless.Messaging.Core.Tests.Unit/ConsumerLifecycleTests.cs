// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class ConsumerLifecycleTests
{
    private readonly Faker _faker = new();

    [Fact]
    public async Task should_call_OnStartingAsync_before_each_message_processing()
    {
        // given
        var services = new ServiceCollection();
        services.AddSingleton<TrackedLifecycleConsumer>();
        services.AddScoped<IConsume<TestMessage>>(sp => sp.GetRequiredService<TrackedLifecycleConsumer>());

        using var serviceProvider = services.BuildServiceProvider();
        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();
        var dispatcher = new CompiledMessageDispatcher(scopeFactory);

        var message = new TestMessage { Id = _faker.Random.Guid(), Content = _faker.Lorem.Sentence() };
        var context = new ConsumeContext<TestMessage>
        {
            Message = message,
            MessageId = _faker.Random.Guid().ToString(),
            CorrelationId = null,
            Headers = new MessageHeader(new Dictionary<string, string?>(StringComparer.Ordinal)),
            Timestamp = DateTimeOffset.UtcNow,
            Topic = "test-topic",
        };

        // when
        await dispatcher.DispatchAsync(context, CancellationToken.None);

        // then
        await using var scope = serviceProvider.CreateAsyncScope();
        var consumer = scope.ServiceProvider.GetRequiredService<TrackedLifecycleConsumer>();

        consumer.OnStartingCallCount.Should().Be(1);
        consumer.ConsumeCallCount.Should().Be(1);
        consumer.OnStartingCalledBeforeConsume.Should().BeTrue();
    }

    [Fact]
    public async Task should_call_OnStoppingAsync_after_each_message_processing()
    {
        // given
        var services = new ServiceCollection();
        services.AddSingleton<TrackedLifecycleConsumer>();
        services.AddScoped<IConsume<TestMessage>>(sp => sp.GetRequiredService<TrackedLifecycleConsumer>());

        using var serviceProvider = services.BuildServiceProvider();
        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();
        var dispatcher = new CompiledMessageDispatcher(scopeFactory);

        var message = new TestMessage { Id = _faker.Random.Guid(), Content = _faker.Lorem.Sentence() };
        var context = new ConsumeContext<TestMessage>
        {
            Message = message,
            MessageId = _faker.Random.Guid().ToString(),
            CorrelationId = null,
            Headers = new MessageHeader(new Dictionary<string, string?>(StringComparer.Ordinal)),
            Timestamp = DateTimeOffset.UtcNow,
            Topic = "test-topic",
        };

        // when
        await dispatcher.DispatchAsync(context, CancellationToken.None);

        // then
        await using var scope = serviceProvider.CreateAsyncScope();
        var consumer = scope.ServiceProvider.GetRequiredService<TrackedLifecycleConsumer>();

        consumer.OnStoppingCallCount.Should().Be(1);
        consumer.ConsumeCallCount.Should().Be(1);
        consumer.OnStoppingCalledAfterConsume.Should().BeTrue();
    }

    [Fact]
    public async Task should_call_lifecycle_hooks_for_each_dispatch()
    {
        // given
        var services = new ServiceCollection();
        services.AddSingleton<TrackedLifecycleConsumer>();
        services.AddScoped<IConsume<TestMessage>>(sp => sp.GetRequiredService<TrackedLifecycleConsumer>());

        using var serviceProvider = services.BuildServiceProvider();
        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();
        var dispatcher = new CompiledMessageDispatcher(scopeFactory);

        var firstContext = new ConsumeContext<TestMessage>
        {
            Message = new TestMessage { Id = _faker.Random.Guid(), Content = _faker.Lorem.Sentence() },
            MessageId = _faker.Random.Guid().ToString(),
            CorrelationId = null,
            Headers = new MessageHeader(new Dictionary<string, string?>(StringComparer.Ordinal)),
            Timestamp = DateTimeOffset.UtcNow,
            Topic = "test-topic",
        };

        var secondContext = new ConsumeContext<TestMessage>
        {
            Message = new TestMessage { Id = _faker.Random.Guid(), Content = _faker.Lorem.Sentence() },
            MessageId = _faker.Random.Guid().ToString(),
            CorrelationId = null,
            Headers = new MessageHeader(new Dictionary<string, string?>(StringComparer.Ordinal)),
            Timestamp = DateTimeOffset.UtcNow,
            Topic = "test-topic",
        };

        // when
        await dispatcher.DispatchAsync(firstContext, CancellationToken.None);
        await dispatcher.DispatchAsync(secondContext, CancellationToken.None);

        // then
        await using var scope = serviceProvider.CreateAsyncScope();
        var consumer = scope.ServiceProvider.GetRequiredService<TrackedLifecycleConsumer>();

        consumer.OnStartingCallCount.Should().Be(2);
        consumer.OnStoppingCallCount.Should().Be(2);
        consumer.ConsumeCallCount.Should().Be(2);
        consumer.OnStartingCalledBeforeConsume.Should().BeTrue();
        consumer.OnStoppingCalledAfterConsume.Should().BeTrue();
    }

    [Fact]
    public async Task should_call_OnStoppingAsync_even_when_consume_throws()
    {
        // given
        var services = new ServiceCollection();
        services.AddSingleton<FailingLifecycleConsumer>();
        services.AddScoped<IConsume<TestMessage>>(sp => sp.GetRequiredService<FailingLifecycleConsumer>());

        using var serviceProvider = services.BuildServiceProvider();
        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();
        var dispatcher = new CompiledMessageDispatcher(scopeFactory);

        var message = new TestMessage { Id = _faker.Random.Guid(), Content = _faker.Lorem.Sentence() };
        var context = new ConsumeContext<TestMessage>
        {
            Message = message,
            MessageId = _faker.Random.Guid().ToString(),
            CorrelationId = null,
            Headers = new MessageHeader(new Dictionary<string, string?>(StringComparer.Ordinal)),
            Timestamp = DateTimeOffset.UtcNow,
            Topic = "test-topic",
        };

        // when
        var act = () => dispatcher.DispatchAsync(context, CancellationToken.None);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Consume failed");

        await using var scope = serviceProvider.CreateAsyncScope();
        var consumer = scope.ServiceProvider.GetRequiredService<FailingLifecycleConsumer>();

        consumer.OnStoppingCalled.Should().BeTrue();
    }

    [Fact]
    public async Task should_not_call_lifecycle_hooks_for_consumers_without_IConsumerLifecycle()
    {
        // given
        var services = new ServiceCollection();
        services.AddScoped<IConsume<TestMessage>, SimpleConsumer>();

        using var serviceProvider = services.BuildServiceProvider();
        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();
        var dispatcher = new CompiledMessageDispatcher(scopeFactory);

        var message = new TestMessage { Id = _faker.Random.Guid(), Content = _faker.Lorem.Sentence() };
        var context = new ConsumeContext<TestMessage>
        {
            Message = message,
            MessageId = _faker.Random.Guid().ToString(),
            CorrelationId = null,
            Headers = new MessageHeader(new Dictionary<string, string?>(StringComparer.Ordinal)),
            Timestamp = DateTimeOffset.UtcNow,
            Topic = "test-topic",
        };

        // when & then - should not throw
        await dispatcher.DispatchAsync(context, CancellationToken.None);
    }

    [Fact]
    public async Task should_suppress_exceptions_from_OnStoppingAsync()
    {
        // given
        var services = new ServiceCollection();
        services.AddSingleton<FailingStoppingConsumer>();
        services.AddScoped<IConsume<TestMessage>>(sp => sp.GetRequiredService<FailingStoppingConsumer>());

        using var serviceProvider = services.BuildServiceProvider();
        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();
        var dispatcher = new CompiledMessageDispatcher(scopeFactory);

        var message = new TestMessage { Id = _faker.Random.Guid(), Content = _faker.Lorem.Sentence() };
        var context = new ConsumeContext<TestMessage>
        {
            Message = message,
            MessageId = _faker.Random.Guid().ToString(),
            CorrelationId = null,
            Headers = new MessageHeader(new Dictionary<string, string?>(StringComparer.Ordinal)),
            Timestamp = DateTimeOffset.UtcNow,
            Topic = "test-topic",
        };

        // when & then - should not throw, exception in OnStoppingAsync is suppressed
        await dispatcher.DispatchAsync(context, CancellationToken.None);

        await using var scope = serviceProvider.CreateAsyncScope();
        var consumer = scope.ServiceProvider.GetRequiredService<FailingStoppingConsumer>();

        consumer.OnStoppingCalled.Should().BeTrue();
    }

    [Fact]
    public async Task should_allow_OnStartingAsync_to_throw_and_propagate_exception()
    {
        // given
        var services = new ServiceCollection();
        services.AddSingleton<FailingStartingConsumer>();
        services.AddScoped<IConsume<TestMessage>>(sp => sp.GetRequiredService<FailingStartingConsumer>());

        using var serviceProvider = services.BuildServiceProvider();
        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();
        var dispatcher = new CompiledMessageDispatcher(scopeFactory);

        var message = new TestMessage { Id = _faker.Random.Guid(), Content = _faker.Lorem.Sentence() };
        var context = new ConsumeContext<TestMessage>
        {
            Message = message,
            MessageId = _faker.Random.Guid().ToString(),
            CorrelationId = null,
            Headers = new MessageHeader(new Dictionary<string, string?>(StringComparer.Ordinal)),
            Timestamp = DateTimeOffset.UtcNow,
            Topic = "test-topic",
        };

        // when
        var act = () => dispatcher.DispatchAsync(context, CancellationToken.None);

        // then - exception from OnStartingAsync should propagate
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Starting failed");

        await using var scope = serviceProvider.CreateAsyncScope();
        var consumer = scope.ServiceProvider.GetRequiredService<FailingStartingConsumer>();

        consumer.ConsumeCalled.Should().BeFalse();
    }

    #region Test Helpers

    private sealed record TestMessage
    {
        public required Guid Id { get; init; }
        public required string Content { get; init; }
    }

    private sealed class TrackedLifecycleConsumer : IConsume<TestMessage>, IConsumerLifecycle
    {
        public int OnStartingCallCount { get; private set; }
        public int OnStoppingCallCount { get; private set; }
        public int ConsumeCallCount { get; private set; }
        public bool OnStartingCalledBeforeConsume { get; private set; } = true;
        public bool OnStoppingCalledAfterConsume { get; private set; } = true;

        public ValueTask OnStartingAsync(CancellationToken cancellationToken)
        {
            OnStartingCallCount++;
            OnStartingCalledBeforeConsume &= ConsumeCallCount == OnStartingCallCount - 1;
            return ValueTask.CompletedTask;
        }

        public ValueTask Consume(ConsumeContext<TestMessage> context, CancellationToken cancellationToken)
        {
            ConsumeCallCount++;
            return ValueTask.CompletedTask;
        }

        public ValueTask OnStoppingAsync(CancellationToken cancellationToken)
        {
            OnStoppingCallCount++;
            OnStoppingCalledAfterConsume &= ConsumeCallCount == OnStoppingCallCount;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FailingLifecycleConsumer : IConsume<TestMessage>, IConsumerLifecycle
    {
        public bool OnStoppingCalled { get; private set; }

        public ValueTask OnStartingAsync(CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask Consume(ConsumeContext<TestMessage> context, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Consume failed");
        }

        public ValueTask OnStoppingAsync(CancellationToken cancellationToken)
        {
            OnStoppingCalled = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class SimpleConsumer : IConsume<TestMessage>
    {
        public ValueTask Consume(ConsumeContext<TestMessage> context, CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FailingStoppingConsumer : IConsume<TestMessage>, IConsumerLifecycle
    {
        public bool OnStoppingCalled { get; private set; }

        public ValueTask OnStartingAsync(CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask Consume(ConsumeContext<TestMessage> context, CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask OnStoppingAsync(CancellationToken cancellationToken)
        {
            OnStoppingCalled = true;
            throw new InvalidOperationException("Stopping failed");
        }
    }

    private sealed class FailingStartingConsumer : IConsume<TestMessage>, IConsumerLifecycle
    {
        public bool ConsumeCalled { get; private set; }

        public ValueTask OnStartingAsync(CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Starting failed");
        }

        public ValueTask Consume(ConsumeContext<TestMessage> context, CancellationToken cancellationToken)
        {
            ConsumeCalled = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask OnStoppingAsync(CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }
    }

    #endregion
}
