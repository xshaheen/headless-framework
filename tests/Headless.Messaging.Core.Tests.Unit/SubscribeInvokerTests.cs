// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Framework.Testing.Tests;
using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

// ReSharper disable AccessToDisposedClosure
public sealed class SubscribeInvokerTests : TestBase
{
    [Fact]
    public async Task should_invoke_consumer_with_compiled_dispatcher()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMessages(messaging =>
        {
            messaging.Consumer<InvokerTestConsumer>().Topic("test.topic").Build();
        });

        var provider = services.BuildServiceProvider();
        var invoker = provider.GetRequiredService<ISubscribeInvoker>();

        var message = new InvokerTestMessage("test-123");
        var mediumMessage = _CreateMediumMessage(message, "test.topic");
        var descriptor = _CreateDescriptor<InvokerTestMessage>();
        var context = new ConsumerContext(descriptor, mediumMessage);

        // when
        var result = await invoker.InvokeAsync(context, AbortToken);

        // then
        result.Should().NotBeNull();
        result.MessageId.Should().Be(mediumMessage.Origin.GetId());
    }

    [Fact]
    public async Task should_deserialize_json_message()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMessages(messaging =>
        {
            messaging.Consumer<InvokerTestConsumer>().Topic("test.topic").Build();
        });

        var provider = services.BuildServiceProvider();
        var invoker = provider.GetRequiredService<ISubscribeInvoker>();

        var message = new InvokerTestMessage("test-456");
        var mediumMessage = _CreateMediumMessage(message, "test.topic");
        var descriptor = _CreateDescriptor<InvokerTestMessage>();
        var context = new ConsumerContext(descriptor, mediumMessage);

        // when
        var result = await invoker.InvokeAsync(context, AbortToken);

        // then
        result.Should().NotBeNull();
        InvokerTestConsumer.LastConsumed.Should().NotBeNull();
        InvokerTestConsumer.LastConsumed.Message.Id.Should().Be("test-456");
    }

    [Fact]
    public async Task should_build_consume_context_with_correct_metadata()
    {
        // given
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
        var mediumMessage = _CreateMediumMessage(message, "test.topic", messageId, correlationId);
        var descriptor = _CreateDescriptor<InvokerTestMessage>();
        var context = new ConsumerContext(descriptor, mediumMessage);

        // when
        await invoker.InvokeAsync(context, AbortToken);

        // then
        var consumed = InvokerTestConsumer.LastConsumed;
        consumed.Should().NotBeNull();
        consumed.MessageId.Should().Be(messageId);
        consumed.CorrelationId.Should().Be(correlationId.ToString());
        consumed.Topic.Should().Be("test.topic");
        consumed.Headers.Should().NotBeNull();
    }

    [Fact]
    public async Task should_throw_when_message_cannot_be_deserialized()
    {
        // given
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

        var descriptor = _CreateDescriptor<InvokerTestMessage>();
        var context = new ConsumerContext(descriptor, mediumMessage);

        // when
        var act = async () => await invoker.InvokeAsync(context);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Failed to deserialize message*");
    }

    [Fact]
    public async Task should_throw_when_consumer_method_missing_consume_context_parameter()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMessages(messaging =>
        {
            messaging.Consumer<InvokerTestConsumer>().Topic("test.topic").Build();
        });

        var provider = services.BuildServiceProvider();
        var invoker = provider.GetRequiredService<ISubscribeInvoker>();

        var message = new InvokerTestMessage("test");
        var mediumMessage = _CreateMediumMessage(message, "test.topic");

        // Manually create descriptor with wrong method
        var badDescriptor = new ConsumerExecutorDescriptor
        {
            ServiceTypeInfo = typeof(InvokerTestConsumer).GetTypeInfo(),
            ImplTypeInfo = typeof(InvokerTestConsumer).GetTypeInfo(),
            MethodInfo = typeof(InvokerTestConsumer).GetMethod(
                nameof(InvokerTestConsumer.BadMethod),
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null
            )!,
            TopicName = "test.topic",
            GroupName = "test",
            Parameters = [],
        };

        var context = new ConsumerContext(badDescriptor, mediumMessage);

        // when
        var act = async () => await invoker.InvokeAsync(context);

        // then
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*Consumer method must have a ConsumeContext<T> parameter*");
    }

    [Fact]
    public async Task should_handle_cancellation_token()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMessages(messaging =>
        {
            messaging.Consumer<CancellableConsumer>().Topic("test.topic").Build();
        });

        var provider = services.BuildServiceProvider();
        var invoker = provider.GetRequiredService<ISubscribeInvoker>();

        var message = new InvokerTestMessage("test");
        var mediumMessage = _CreateMediumMessage(message, "test.topic");
        var descriptor = _CreateDescriptor<InvokerTestMessage, CancellableConsumer>();
        var context = new ConsumerContext(descriptor, mediumMessage);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // when
        var act = async () => await invoker.InvokeAsync(context, cts.Token);

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_handle_nullable_correlation_id()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMessages(messaging =>
        {
            messaging.Consumer<InvokerTestConsumer>().Topic("test.topic").Build();
        });

        var provider = services.BuildServiceProvider();
        var invoker = provider.GetRequiredService<ISubscribeInvoker>();

        var message = new InvokerTestMessage("test");
        var mediumMessage = _CreateMediumMessage(message, "test.topic", correlationId: null);
        var descriptor = _CreateDescriptor<InvokerTestMessage>();
        var context = new ConsumerContext(descriptor, mediumMessage);

        // when
        await invoker.InvokeAsync(context, AbortToken);

        // then
        var consumed = InvokerTestConsumer.LastConsumed;
        consumed.Should().NotBeNull();
        consumed.CorrelationId.Should().BeNull();
    }

    private static MediumMessage _CreateMediumMessage<T>(
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

    private static ConsumerExecutorDescriptor _CreateDescriptor<TMessage, TConsumer>()
        where TMessage : class
        where TConsumer : IConsume<TMessage>
    {
        var consumeMethod = typeof(IConsume<TMessage>).GetMethod(
            nameof(IConsume<>.Consume),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
            null,
            [typeof(ConsumeContext<TMessage>), typeof(CancellationToken)],
            null
        )!;

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
                    IsFromMessaging = p.ParameterType == typeof(CancellationToken),
                })
                .ToList(),
        };
    }

    private static ConsumerExecutorDescriptor _CreateDescriptor<TMessage>()
        where TMessage : class
    {
        // For tests, we assume InvokerTestConsumer handles InvokerTestMessage
        var consumeMethod = typeof(IConsume<TMessage>).GetMethod(
            nameof(IConsume<>.Consume),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
            null,
            [typeof(ConsumeContext<TMessage>), typeof(CancellationToken)],
            null
        )!;

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
                    IsFromMessaging = p.ParameterType == typeof(CancellationToken),
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

    public ValueTask Consume(ConsumeContext<InvokerTestMessage> context, CancellationToken cancellationToken)
    {
        LastConsumed = context;
        return ValueTask.CompletedTask;
    }

#pragma warning disable CA1822 // Intended to be an instance method for testing purposes
    public void BadMethod()
#pragma warning restore CA1822
    {
        // Missing ConsumeContext parameter
    }
}

public sealed class CancellableConsumer : IConsume<InvokerTestMessage>
{
    public ValueTask Consume(ConsumeContext<InvokerTestMessage> context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }
}
