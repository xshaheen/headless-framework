// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Messaging;
using Headless.Messaging.CircuitBreaker;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.Messaging.Retry;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tests;

/// <summary>
/// Tests that <see cref="SubscribeExecutor"/> correctly distinguishes between handler-timeout
/// cancellations (TaskCanceledException where IsCancellationRequested = false) and
/// app-shutdown cancellations (OperationCanceledException where IsCancellationRequested = true).
/// </summary>
public sealed class SubscribeExecutorCancellationTests : TestBase
{
    private static MediumMessage _CreateMediumMessage(string topic = "test.topic")
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = Guid.NewGuid().ToString(),
            [Headers.MessageName] = topic,
        };

        return new MediumMessage
        {
            StorageId = 1L,
            Origin = new Message(headers, "{}"),
            Content = "{}",
            Added = DateTime.UtcNow,
        };
    }

    private static ConsumerExecutorDescriptor _CreateDescriptor()
    {
        var consumeMethod = typeof(IConsume<CancellationExecutorTestMessage>).GetMethod(
            nameof(IConsume<CancellationExecutorTestMessage>.Consume),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
            null,
            [typeof(ConsumeContext<CancellationExecutorTestMessage>), typeof(CancellationToken)],
            null
        )!;

        return new ConsumerExecutorDescriptor
        {
            ServiceTypeInfo = typeof(CancellationExecutorTestConsumer).GetTypeInfo(),
            ImplTypeInfo = typeof(CancellationExecutorTestConsumer).GetTypeInfo(),
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

    private SubscribeExecutor _CreateExecutor(
        ISubscribeInvoker invoker,
        IDataStorage storage,
        MessagingOptions? messagingOptions = null,
        ICircuitBreakerStateManager? circuitBreaker = null
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(x =>
        {
            x.Subscribe<CancellationExecutorTestConsumer>().Topic("test.topic");
            x.UseInMemoryMessageQueue();
            x.UseInMemoryStorage();
        });

        var provider = services.BuildServiceProvider();
        var logger = provider.GetRequiredService<ILogger<SubscribeExecutor>>();
        var options = Options.Create(
            messagingOptions
                ?? new MessagingOptions
                {
                    RetryPolicy =
                    {
                        MaxAttempts = 1,
                        BackoffStrategy = new FixedIntervalBackoffStrategy(TimeSpan.Zero),
                    },
                }
        );

        circuitBreaker ??= Substitute.For<ICircuitBreakerStateManager>();
        return new SubscribeExecutor(provider, storage, invoker, TimeProvider.System, logger, options, circuitBreaker);
    }

    [Fact]
    public async Task TaskCanceledException_WithoutRequestedToken_ShouldPropagate_As_Failed()
    {
        // given — simulate HttpClient / downstream timeout:
        //   TaskCanceledException where CancellationToken.IsCancellationRequested = false
        var storage = Substitute.For<IDataStorage>();
        storage
            .ChangeReceiveStateAsync(Arg.Any<MediumMessage>(), Arg.Any<StatusName>())
            .Returns(ValueTask.CompletedTask);

        var invoker = Substitute.For<ISubscribeInvoker>();
        // A CancellationTokenSource that has NOT been cancelled → IsCancellationRequested = false
        using var cts = new CancellationTokenSource();
        var timeoutTce = new TaskCanceledException("HttpClient timeout", null, cts.Token);
        invoker
            .InvokeAsync(Arg.Any<ConsumerContext>(), Arg.Any<CancellationToken>())
            .Returns<Task<ConsumerExecutedResult>>(_ => Task.FromException<ConsumerExecutedResult>(timeoutTce));

        var executor = _CreateExecutor(invoker, storage);
        var message = _CreateMediumMessage();
        var descriptor = _CreateDescriptor();

        // when
        var result = await executor.ExecuteAsync(message, descriptor, CancellationToken.None);

        // then — must be a failure, not swallowed
        result.Succeeded.Should().BeFalse();
        await storage.Received().ChangeReceiveStateAsync(Arg.Any<MediumMessage>(), StatusName.Failed);
    }

    [Fact]
    public async Task OperationCanceledException_WithRequestedToken_ShouldBePersisted_As_Failed()
    {
        // given — simulate app-shutdown cancellation:
        //   OperationCanceledException where CancellationToken.IsCancellationRequested = true
        var storage = Substitute.For<IDataStorage>();
        storage
            .ChangeReceiveStateAsync(Arg.Any<MediumMessage>(), Arg.Any<StatusName>())
            .Returns(ValueTask.CompletedTask);

        var invoker = Substitute.For<ISubscribeInvoker>();

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync(); // token IS requested
        var shutdownOce = new OperationCanceledException("App shutdown", cts.Token);
        invoker
            .InvokeAsync(Arg.Any<ConsumerContext>(), Arg.Any<CancellationToken>())
            .Returns<Task<ConsumerExecutedResult>>(_ => Task.FromException<ConsumerExecutedResult>(shutdownOce));

        var executor = _CreateExecutor(invoker, storage);
        var message = _CreateMediumMessage();
        var descriptor = _CreateDescriptor();

        // when
        var result = await executor.ExecuteAsync(message, descriptor, CancellationToken.None);

        // then — persisted as failed so it can be retried after restart
        result.Succeeded.Should().BeFalse();
        message.Retries.Should().Be(1);
        await storage.Received().ChangeReceiveStateAsync(Arg.Any<MediumMessage>(), StatusName.Failed);
    }

    [Fact]
    public async Task TaskCanceledException_WithRequestedToken_ShouldBePersisted_As_Failed()
    {
        // given — TaskCanceledException but the token IS requested (e.g. handler respected shutdown CT)
        var storage = Substitute.For<IDataStorage>();
        storage
            .ChangeReceiveStateAsync(Arg.Any<MediumMessage>(), Arg.Any<StatusName>())
            .Returns(ValueTask.CompletedTask);

        var invoker = Substitute.For<ISubscribeInvoker>();

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync(); // token IS requested
        var requestedTce = new TaskCanceledException("Cancelled by shutdown", null, cts.Token);
        invoker
            .InvokeAsync(Arg.Any<ConsumerContext>(), Arg.Any<CancellationToken>())
            .Returns<Task<ConsumerExecutedResult>>(_ => Task.FromException<ConsumerExecutedResult>(requestedTce));

        var executor = _CreateExecutor(invoker, storage);
        var message = _CreateMediumMessage();
        var descriptor = _CreateDescriptor();

        // when
        var result = await executor.ExecuteAsync(message, descriptor, CancellationToken.None);

        // then — persisted as failed so it can be retried after restart
        result.Succeeded.Should().BeFalse();
        message.Retries.Should().Be(1);
        await storage.Received().ChangeReceiveStateAsync(Arg.Any<MediumMessage>(), StatusName.Failed);
    }
}

// Supporting types

public sealed record CancellationExecutorTestMessage(string Id);

public sealed class CancellationExecutorTestConsumer : IConsume<CancellationExecutorTestMessage>
{
    public ValueTask Consume(
        ConsumeContext<CancellationExecutorTestMessage> context,
        CancellationToken cancellationToken
    )
    {
        return ValueTask.CompletedTask;
    }
}
