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
    private static readonly IServiceProvider _EmptyScope = new ServiceCollection().BuildServiceProvider();

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
        services.AddHeadlessMessaging(setup =>
        {
            setup.Subscribe<CancellationExecutorTestConsumer>().Topic("test.topic");
            setup.UseInMemoryMessageQueue();
            setup.UseInMemoryStorage();
        });

        var provider = services.BuildServiceProvider();
        var logger = provider.GetRequiredService<ILogger<SubscribeExecutor>>();
        var options = Options.Create(
            messagingOptions
                ?? new MessagingOptions
                {
                    RetryPolicy =
                    {
                        MaxInlineRetries = 0,
                        MaxPersistedRetries = 0,
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
            .Returns(ValueTask.FromResult(true));

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
        var result = await executor.ExecuteAsync(message, _EmptyScope, descriptor, CancellationToken.None);

        // then — must be a failure, not swallowed
        result.Succeeded.Should().BeFalse();
        await storage.Received().ChangeReceiveStateAsync(Arg.Any<MediumMessage>(), StatusName.Failed);
    }

    [Fact]
    public async Task OperationCanceledException_WithRequestedToken_ShouldNotWriteState()
    {
        // given — simulate app-shutdown cancellation:
        //   OperationCanceledException where CancellationToken.IsCancellationRequested = true.
        // The executor must classify this as cancellation (RetryDecision.Stop), NOT invoke
        // OnExhausted, and NOT write a state transition. The row keeps its prior NextRetryAt and
        // the persisted retry processor picks it up on restart.
        var storage = Substitute.For<IDataStorage>();
        storage
            .ChangeReceiveStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(true));

        var invoker = Substitute.For<ISubscribeInvoker>();

        using var cts = new CancellationTokenSource();
        var callbackInvoked = false;
        invoker
            .InvokeAsync(Arg.Any<ConsumerContext>(), Arg.Any<CancellationToken>())
            .Returns<Task<ConsumerExecutedResult>>(async _ =>
            {
                await cts.CancelAsync();
                var shutdownOce = new OperationCanceledException("App shutdown", cts.Token);
                throw shutdownOce;
            });

        var executor = _CreateExecutor(
            invoker,
            storage,
            new MessagingOptions
            {
                RetryPolicy =
                {
                    MaxInlineRetries = 0,
                    MaxPersistedRetries = 0,
                    BackoffStrategy = new FixedIntervalBackoffStrategy(TimeSpan.Zero),
                    OnExhausted = (_, _) =>
                    {
                        callbackInvoked = true;
                        return Task.CompletedTask;
                    },
                },
            }
        );
        var message = _CreateMediumMessage();
        var descriptor = _CreateDescriptor();

        // when
        var result = await executor.ExecuteAsync(message, _EmptyScope, descriptor, cts.Token);

        // then — Shutdown OCE: no state write, no OnExhausted. Row keeps prior NextRetryAt/Status.
        result.Succeeded.Should().BeFalse();
        message.Retries.Should().Be(0);
        callbackInvoked.Should().BeFalse();
        await storage
            .DidNotReceive()
            .ChangeReceiveStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task TaskCanceledException_WithRequestedToken_ShouldNotWriteState()
    {
        // given — TaskCanceledException but the token IS requested (handler respected shutdown CT).
        // X4 invariant: shutdown-classified cancellations leave the row untouched so the persisted
        // retry processor picks it up on restart.
        var storage = Substitute.For<IDataStorage>();
        storage
            .ChangeReceiveStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(true));

        var invoker = Substitute.For<ISubscribeInvoker>();

        using var cts = new CancellationTokenSource();
        invoker
            .InvokeAsync(Arg.Any<ConsumerContext>(), Arg.Any<CancellationToken>())
            .Returns<Task<ConsumerExecutedResult>>(async _ =>
            {
                await cts.CancelAsync();
                var requestedTce = new TaskCanceledException("Cancelled by shutdown", null, cts.Token);
                throw requestedTce;
            });

        var executor = _CreateExecutor(invoker, storage);
        var message = _CreateMediumMessage();
        var descriptor = _CreateDescriptor();

        // when
        var result = await executor.ExecuteAsync(message, _EmptyScope, descriptor, cts.Token);

        // then — Shutdown OCE: no state write. Row keeps prior NextRetryAt/Status.
        result.Succeeded.Should().BeFalse();
        message.Retries.Should().Be(0);
        await storage
            .DidNotReceive()
            .ChangeReceiveStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>()
            );
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
