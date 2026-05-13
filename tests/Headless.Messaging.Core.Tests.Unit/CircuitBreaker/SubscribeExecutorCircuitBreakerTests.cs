// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Messaging;
using Headless.Messaging.CircuitBreaker;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tests.CircuitBreaker;

/// <summary>
/// Verifies that <see cref="SubscribeExecutor"/> reports failures and successes
/// to <see cref="ICircuitBreakerStateManager"/> at the correct times.
/// </summary>
public sealed class SubscribeExecutorCircuitBreakerTests : TestBase
{
    private const string _TopicName = "cb.test.topic";
    private const string _GroupName = "cb.test.group";

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static MediumMessage _CreateMediumMessage()
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = Guid.NewGuid().ToString(),
            [Headers.MessageName] = _TopicName,
            [Headers.Group] = _GroupName,
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
        var consumeMethod = typeof(IConsume<CbTestMessage>).GetMethod(
            nameof(IConsume<CbTestMessage>.Consume),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
            null,
            [typeof(ConsumeContext<CbTestMessage>), typeof(CancellationToken)],
            null
        )!;

        return new ConsumerExecutorDescriptor
        {
            ServiceTypeInfo = typeof(CbTestConsumer).GetTypeInfo(),
            ImplTypeInfo = typeof(CbTestConsumer).GetTypeInfo(),
            MethodInfo = consumeMethod,
            TopicName = _TopicName,
            GroupName = _GroupName,
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

    private static (SubscribeExecutor executor, ICircuitBreakerStateManager cbMock) _CreateExecutor(
        ISubscribeInvoker invoker,
        IDataStorage storage
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(x =>
        {
            x.Subscribe<CbTestConsumer>().Topic(_TopicName);
            x.UseInMemoryMessageQueue();
            x.UseInMemoryStorage();
        });

        var provider = services.BuildServiceProvider();
        var logger = provider.GetRequiredService<ILogger<SubscribeExecutor>>();
        var options = Options.Create(new MessagingOptions { FailedRetryCount = 0 });
        var circuitBreaker = Substitute.For<ICircuitBreakerStateManager>();

        var executor = new SubscribeExecutor(
            provider,
            storage,
            invoker,
            TimeProvider.System,
            logger,
            options,
            circuitBreaker
        );

        return (executor, circuitBreaker);
    }

    private static IDataStorage _CreateStorage()
    {
        var storage = Substitute.For<IDataStorage>();
        storage
            .ChangeReceiveStateAsync(Arg.Any<MediumMessage>(), Arg.Any<StatusName>())
            .Returns(ValueTask.CompletedTask);
        return storage;
    }

    // -------------------------------------------------------------------------
    // Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task should_report_failure_to_state_manager_when_handler_throws()
    {
        // given
        var storage = _CreateStorage();
        var invoker = Substitute.For<ISubscribeInvoker>();
        var original = new TimeoutException("downstream timeout");
        invoker
            .InvokeAsync(Arg.Any<ConsumerContext>(), Arg.Any<CancellationToken>())
            .Returns<Task<ConsumerExecutedResult>>(_ => Task.FromException<ConsumerExecutedResult>(original));

        var (executor, cbMock) = _CreateExecutor(invoker, storage);

        // when
        await executor.ExecuteAsync(_CreateMediumMessage(), _CreateDescriptor(), CancellationToken.None);

        // then
        await cbMock.Received(1).ReportFailureAsync(_GroupName, original);
    }

    [Fact]
    public async Task should_report_success_to_state_manager_when_handler_succeeds()
    {
        // given
        var storage = _CreateStorage();
        var invoker = Substitute.For<ISubscribeInvoker>();
        invoker
            .InvokeAsync(Arg.Any<ConsumerContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ConsumerExecutedResult(null, "msg-1", null, null)));

        var (executor, cbMock) = _CreateExecutor(invoker, storage);

        // when
        await executor.ExecuteAsync(_CreateMediumMessage(), _CreateDescriptor(), CancellationToken.None);

        // then
        await cbMock.Received(1).ReportSuccessAsync(_GroupName);
    }

    [Fact]
    public async Task should_report_unwrapped_inner_exception_not_wrapper()
    {
        // given — SubscribeExecutor wraps handler exceptions in SubscriberExecutionFailedException;
        //   the circuit breaker must see the original exception for transient classification.
        var storage = _CreateStorage();
        var invoker = Substitute.For<ISubscribeInvoker>();
        var original = new HttpRequestException("connection refused");
        invoker
            .InvokeAsync(Arg.Any<ConsumerContext>(), Arg.Any<CancellationToken>())
            .Returns<Task<ConsumerExecutedResult>>(_ => Task.FromException<ConsumerExecutedResult>(original));

        var (executor, cbMock) = _CreateExecutor(invoker, storage);

        // when
        await executor.ExecuteAsync(_CreateMediumMessage(), _CreateDescriptor(), CancellationToken.None);

        // then — must receive HttpRequestException, not SubscriberExecutionFailedException
        await cbMock.Received(1).ReportFailureAsync(_GroupName, Arg.Is<Exception>(e => e is HttpRequestException));
    }

    [Fact]
    public async Task should_still_persist_db_state_when_circuit_breaker_is_wired()
    {
        // given
        var storage = _CreateStorage();
        var invoker = Substitute.For<ISubscribeInvoker>();
        var original = new InvalidOperationException("boom");
        invoker
            .InvokeAsync(Arg.Any<ConsumerContext>(), Arg.Any<CancellationToken>())
            .Returns<Task<ConsumerExecutedResult>>(_ => Task.FromException<ConsumerExecutedResult>(original));

        var (executor, _) = _CreateExecutor(invoker, storage);

        // when
        await executor.ExecuteAsync(_CreateMediumMessage(), _CreateDescriptor(), CancellationToken.None);

        // then — DB state must still be persisted
        await storage.Received().ChangeReceiveStateAsync(Arg.Any<MediumMessage>(), StatusName.Failed);
    }

    [Fact]
    public async Task should_persist_db_success_state_and_report_to_circuit_breaker()
    {
        // given
        var storage = _CreateStorage();
        var invoker = Substitute.For<ISubscribeInvoker>();
        invoker
            .InvokeAsync(Arg.Any<ConsumerContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ConsumerExecutedResult(null, "msg-1", null, null)));

        var (executor, cbMock) = _CreateExecutor(invoker, storage);

        // when
        await executor.ExecuteAsync(_CreateMediumMessage(), _CreateDescriptor(), CancellationToken.None);

        // then — both DB persistence and circuit breaker reporting happen
        await storage.Received().ChangeReceiveStateAsync(Arg.Any<MediumMessage>(), StatusName.Succeeded);
        await cbMock.Received(1).ReportSuccessAsync(_GroupName);
    }
}

// Supporting types

public sealed record CbTestMessage(string Id);

public sealed class CbTestConsumer : IConsume<CbTestMessage>
{
    public ValueTask Consume(ConsumeContext<CbTestMessage> context, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}
