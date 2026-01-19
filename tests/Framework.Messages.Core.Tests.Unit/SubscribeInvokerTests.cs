// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using System.Text.Json;
using Framework.Messages;
using Framework.Messages.Internal;
using Framework.Messages.Messages;
using Framework.Messages.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public class SubscribeInvokerTests
{
    [Fact]
    public async Task should_invoke_consumer_with_compiled_dispatcher()
    {
        // Given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMessages(messaging =>
        {
            messaging.Consumer<InvokerTestConsumer>().Topic("test.topic").Build();
        });

        var provider = services.BuildServiceProvider();
        var invoker = provider.GetRequiredService<ISubscribeInvoker>();

        var message = new InvokerTestMessage("test-123");
        var mediumMessage = CreateMediumMessage(message, "test.topic");
        var descriptor = CreateDescriptor<InvokerTestMessage>();
        var context = new ConsumerContext(descriptor, mediumMessage);

        // When
        var result = await invoker.InvokeAsync(context);

        // Then
        result.Should().NotBeNull();
        result.MessageId.Should().Be(mediumMessage.Origin.GetId());
    }

    [Fact]
    public async Task should_deserialize_json_message()
    {
        // Given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMessages(messaging =>
        {
            messaging.Consumer<InvokerTestConsumer>().Topic("test.topic").Build();
        });

        var provider = services.BuildServiceProvider();
        var invoker = provider.GetRequiredService<ISubscribeInvoker>();

        var message = new InvokerTestMessage("test-456");
        var mediumMessage = CreateMediumMessage(message, "test.topic");
        var descriptor = CreateDescriptor<InvokerTestMessage>();
        var context = new ConsumerContext(descriptor, mediumMessage);

        // When
        var result = await invoker.InvokeAsync(context);

        // Then
        result.Should().NotBeNull();
        InvokerTestConsumer.LastConsumed.Should().NotBeNull();
        InvokerTestConsumer.LastConsumed!.Message.Id.Should().Be("test-456");
    }

    [Fact]
    public async Task should_build_consume_context_with_correct_metadata()
    {
        // Given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMessages(messaging =>
        {
            messaging.Consumer<InvokerTestConsumer>().Topic("test.topic").Build();
        });

        var provider = services.BuildServiceProvider();
        var invoker = provider.GetRequiredService<ISubscribeInvoker>();

        var message = new InvokerTestMessage("test-789");
        var messageId = Guid.NewGuid().ToString();
        var correlationId = Guid.NewGuid();
        var mediumMessage = CreateMediumMessage(message, "test.topic", messageId, correlationId);
        var descriptor = CreateDescriptor<InvokerTestMessage>();
        var context = new ConsumerContext(descriptor, mediumMessage);

        // When
        await invoker.InvokeAsync(context);

        // Then
        var consumed = InvokerTestConsumer.LastConsumed;
        consumed.Should().NotBeNull();
        consumed!.MessageId.Should().Be(messageId);
        consumed.CorrelationId.Should().Be(correlationId.ToString());
        consumed.Topic.Should().Be("test.topic");
        consumed.Headers.Should().NotBeNull();
    }

    [Fact]
    public async Task should_throw_when_message_cannot_be_deserialized()
    {
        // Given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMessages(messaging =>
        {
            messaging.Consumer<InvokerTestConsumer>().Topic("test.topic").Build();
        });

        var provider = services.BuildServiceProvider();
        var invoker = provider.GetRequiredService<ISubscribeInvoker>();

        var mediumMessage = new MediumMessage
        {
            DbId = "1",
            Origin = new Message(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    [Headers.MessageId] = Guid.NewGuid().ToString(),
                    [Headers.MessageName] = "test.topic",
                },
                null
            ), // null value
            Content = string.Empty,
            Added = DateTime.UtcNow,
        };

        var descriptor = CreateDescriptor<InvokerTestMessage>();
        var context = new ConsumerContext(descriptor, mediumMessage);

        // When
        var act = async () => await invoker.InvokeAsync(context);

        // Then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Failed to deserialize message*");
    }

    [Fact]
    public async Task should_throw_when_consumer_method_missing_consume_context_parameter()
    {
        // Given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMessages(messaging =>
        {
            messaging.Consumer<InvokerTestConsumer>().Topic("test.topic").Build();
        });

        var provider = services.BuildServiceProvider();
        var invoker = provider.GetRequiredService<ISubscribeInvoker>();

        var message = new InvokerTestMessage("test");
        var mediumMessage = CreateMediumMessage(message, "test.topic");

        // Manually create descriptor with wrong method
        var badDescriptor = new ConsumerExecutorDescriptor
        {
            ServiceTypeInfo = typeof(InvokerTestConsumer).GetTypeInfo(),
            ImplTypeInfo = typeof(InvokerTestConsumer).GetTypeInfo(),
            MethodInfo = typeof(InvokerTestConsumer).GetMethod("BadMethod")!,
            TopicName = "test.topic",
            GroupName = "test",
            Parameters = [],
        };

        var context = new ConsumerContext(badDescriptor, mediumMessage);

        // When
        var act = async () => await invoker.InvokeAsync(context);

        // Then
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*Consumer method must have a ConsumeContext<T> parameter*");
    }

    [Fact]
    public async Task should_handle_cancellation_token()
    {
        // Given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMessages(messaging =>
        {
            messaging.Consumer<CancellableConsumer>().Topic("test.topic").Build();
        });

        var provider = services.BuildServiceProvider();
        var invoker = provider.GetRequiredService<ISubscribeInvoker>();

        var message = new InvokerTestMessage("test");
        var mediumMessage = CreateMediumMessage(message, "test.topic");
        var descriptor = CreateDescriptor<InvokerTestMessage, CancellableConsumer>();
        var context = new ConsumerContext(descriptor, mediumMessage);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // When
        var act = async () => await invoker.InvokeAsync(context, cts.Token);

        // Then
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_handle_nullable_correlation_id()
    {
        // Given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMessages(messaging =>
        {
            messaging.Consumer<InvokerTestConsumer>().Topic("test.topic").Build();
        });

        var provider = services.BuildServiceProvider();
        var invoker = provider.GetRequiredService<ISubscribeInvoker>();

        var message = new InvokerTestMessage("test");
        var mediumMessage = CreateMediumMessage(message, "test.topic", correlationId: null);
        var descriptor = CreateDescriptor<InvokerTestMessage>();
        var context = new ConsumerContext(descriptor, mediumMessage);

        // When
        await invoker.InvokeAsync(context);

        // Then
        var consumed = InvokerTestConsumer.LastConsumed;
        consumed.Should().NotBeNull();
        consumed!.CorrelationId.Should().BeNull();
    }

    private static MediumMessage CreateMediumMessage<T>(
        T message,
        string topicName,
        string? messageId = null,
        Guid? correlationId = null
    )
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = messageId ?? Guid.NewGuid().ToString(),
            [Headers.MessageName] = topicName,
        };

        if (correlationId.HasValue)
        {
            headers[Headers.CorrelationId] = correlationId.Value.ToString();
        }

        var json = JsonSerializer.Serialize(message);

        return new MediumMessage
        {
            DbId = "1",
            Origin = new Message(headers, json),
            Content = json,
            Added = DateTime.UtcNow,
        };
    }

    private static ConsumerExecutorDescriptor CreateDescriptor<TMessage, TConsumer>()
        where TMessage : class
        where TConsumer : IConsume<TMessage>
    {
        var consumeMethod = typeof(IConsume<TMessage>).GetMethod(nameof(IConsume<TMessage>.Consume))!;

        return new ConsumerExecutorDescriptor
        {
            ServiceTypeInfo = typeof(TConsumer).GetTypeInfo(),
            ImplTypeInfo = typeof(TConsumer).GetTypeInfo(),
            MethodInfo = consumeMethod,
            TopicName = "test.topic",
            GroupName = "test",
            Parameters = consumeMethod
                .GetParameters()
                .Select(p => new ParameterDescriptor
                {
                    Name = p.Name!,
                    ParameterType = p.ParameterType,
                    IsFromCap = p.ParameterType == typeof(CancellationToken),
                })
                .ToList(),
        };
    }

    private static ConsumerExecutorDescriptor CreateDescriptor<TMessage>()
        where TMessage : class
    {
        // For tests, we assume InvokerTestConsumer handles InvokerTestMessage
        var consumeMethod = typeof(IConsume<TMessage>).GetMethod(nameof(IConsume<InvokerTestMessage>.Consume))!;

        return new ConsumerExecutorDescriptor
        {
            ServiceTypeInfo = typeof(InvokerTestConsumer).GetTypeInfo(),
            ImplTypeInfo = typeof(InvokerTestConsumer).GetTypeInfo(),
            MethodInfo = consumeMethod,
            TopicName = "test.topic",
            GroupName = "test",
            Parameters = consumeMethod
                .GetParameters()
                .Select(p => new ParameterDescriptor
                {
                    Name = p.Name!,
                    ParameterType = p.ParameterType,
                    IsFromCap = p.ParameterType == typeof(CancellationToken),
                })
                .ToList(),
        };
    }
}

// Test message and consumers
public sealed record InvokerTestMessage(string Id);

public sealed class InvokerTestConsumer : IConsume<InvokerTestMessage>
{
    public static ConsumeContext<InvokerTestMessage>? LastConsumed { get; private set; }

    public ValueTask Consume(ConsumeContext<InvokerTestMessage> context, CancellationToken cancellationToken = default)
    {
        LastConsumed = context;
        return ValueTask.CompletedTask;
    }

    public void BadMethod()
    {
        // Missing ConsumeContext parameter
    }
}

public sealed class CancellableConsumer : IConsume<InvokerTestMessage>
{
    public ValueTask Consume(ConsumeContext<InvokerTestMessage> context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }
}
