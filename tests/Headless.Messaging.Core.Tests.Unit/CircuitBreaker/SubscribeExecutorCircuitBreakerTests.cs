// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Messaging;
using Headless.Messaging.CircuitBreaker;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
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
    private const string _MessageName = "cb.test.messageName";
    private const string _GroupName = "cb.test.group";
    private const string _CircuitBreakerGroupName = "0:cb.test.group";

    private static readonly IServiceProvider _EmptyScope = new ServiceCollection().BuildServiceProvider();

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static MediumMessage _CreateMediumMessage()
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = Guid.NewGuid().ToString(),
            [Headers.MessageName] = _MessageName,
            [Headers.Group] = _GroupName,
        };

        return new MediumMessage
        {
            StorageId = Guid.NewGuid(),
            Origin = new Message(headers, "{}"),
            Content = "{}",
            IntentType = IntentType.Bus,
            Added = DateTimeOffset.UtcNow,
        };
    }

    private static ConsumerExecutorDescriptor _CreateDescriptor()
    {
        var consumeMethod = typeof(IConsume<CbTestMessage>).GetMethod(
            nameof(IConsume<>.ConsumeAsync),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
            null,
            [typeof(ConsumeContext<CbTestMessage>), typeof(CancellationToken)],
            null
        )!;

        return new ConsumerExecutorDescriptor
        {
            IntentType = IntentType.Bus,
            ServiceTypeInfo = typeof(CbTestConsumer).GetTypeInfo(),
            ImplTypeInfo = typeof(CbTestConsumer).GetTypeInfo(),
            MethodInfo = consumeMethod,
            MessageName = _MessageName,
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
        storage
            .LeaseReceiveAsync(Arg.Any<MediumMessage>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(true));
        storage
            .LeaseReceiveAndReserveAttemptAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(true));
        storage
            .ReserveReceiveAttemptAsync(Arg.Any<MediumMessage>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(true));
        storage
            .ChangeReceiveRetryStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(true));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(setup =>
        {
            setup.ForMessage<CbTestMessage>(message => message.MessageName(_MessageName).OnBus<CbTestConsumer>());
            setup.UseInMemory();
            setup.UseInMemoryStorage();
        });

        var provider = services.BuildServiceProvider();
        var logger = provider.GetRequiredService<ILogger<SubscribeExecutor>>();
        var options = Options.Create(
            new MessagingOptions
            {
                RetryPolicy = { RetryStrategy = TestRetryStrategies.ZeroDelay(0), MaxPersistedRetries = 0 },
            }
        );
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
        // Use Arg.Any<>() on every parameter — including the optional ones.
        // NSubstitute records optional defaults as exact-value matchers, which makes
        // setups silently miss when callers pass a non-default nextRetryAt, lockedUntil,
        // originalRetries, or CT. #7 added originalRetries to every state-write site.
        storage
            .ChangeReceiveStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(true));
        storage
            .ChangeReceiveRetryStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(true));
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
        await executor.ExecuteAsync(_CreateMediumMessage(), _EmptyScope, _CreateDescriptor(), AbortToken);

        // then
        await cbMock.Received(1).ReportFailureAsync(_CircuitBreakerGroupName, original, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_report_success_to_state_manager_when_handler_succeeds()
    {
        // given
        var storage = _CreateStorage();
        var invoker = Substitute.For<ISubscribeInvoker>();
        invoker
            .InvokeAsync(Arg.Any<ConsumerContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ConsumerExecutedResult(null, null, "msg-1", null, null)));

        var (executor, cbMock) = _CreateExecutor(invoker, storage);

        // when
        await executor.ExecuteAsync(_CreateMediumMessage(), _EmptyScope, _CreateDescriptor(), AbortToken);

        // then
        await cbMock.Received(1).ReportSuccessAsync(_CircuitBreakerGroupName, CancellationToken.None);
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
        await executor.ExecuteAsync(_CreateMediumMessage(), _EmptyScope, _CreateDescriptor(), AbortToken);

        // then — must receive HttpRequestException, not SubscriberExecutionFailedException
        await cbMock
            .Received(1)
            .ReportFailureAsync(
                _CircuitBreakerGroupName,
                Arg.Is<Exception>(e => e is HttpRequestException),
                Arg.Any<CancellationToken>()
            );
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
        await executor.ExecuteAsync(_CreateMediumMessage(), _EmptyScope, _CreateDescriptor(), AbortToken);

        // then — DB state must still be persisted
        await storage
            .Received(1)
            .ChangeReceiveRetryStateAsync(
                Arg.Any<MediumMessage>(),
                StatusName.Failed,
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_release_half_open_probe_when_receive_lease_is_not_acquired()
    {
        // given
        var storage = _CreateStorage();
        var invoker = Substitute.For<ISubscribeInvoker>();
        var (executor, cbMock) = _CreateExecutor(invoker, storage);

        storage
            .LeaseReceiveAndReserveAttemptAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(false));

        // when
        await executor.ExecuteAsync(_CreateMediumMessage(), _EmptyScope, _CreateDescriptor(), AbortToken);

        // then
        cbMock.Received(1).ReleaseHalfOpenProbe(_CircuitBreakerGroupName);
        await cbMock.DidNotReceiveWithAnyArgs().ReportSuccessAsync(default!, AbortToken);
        await cbMock.DidNotReceiveWithAnyArgs().ReportFailureAsync(default!, default!, AbortToken);
    }

    [Fact]
    public async Task should_release_half_open_probe_when_failed_state_write_is_already_terminal()
    {
        // given
        var storage = _CreateStorage();
        storage
            .ChangeReceiveRetryStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(false));
        var invoker = Substitute.For<ISubscribeInvoker>();
        invoker
            .InvokeAsync(Arg.Any<ConsumerContext>(), Arg.Any<CancellationToken>())
            .Returns<Task<ConsumerExecutedResult>>(_ =>
                Task.FromException<ConsumerExecutedResult>(new TimeoutException("downstream timeout"))
            );
        var (executor, cbMock) = _CreateExecutor(invoker, storage);
        storage
            .ChangeReceiveRetryStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(false));

        // when
        await executor.ExecuteAsync(_CreateMediumMessage(), _EmptyScope, _CreateDescriptor(), AbortToken);

        // then
        cbMock.Received(1).ReleaseHalfOpenProbe(_CircuitBreakerGroupName);
        await cbMock.DidNotReceiveWithAnyArgs().ReportSuccessAsync(default!, AbortToken);
        await cbMock.DidNotReceiveWithAnyArgs().ReportFailureAsync(default!, default!, AbortToken);
    }

    [Fact]
    public async Task should_persist_db_success_state_and_report_to_circuit_breaker()
    {
        // given
        var storage = _CreateStorage();
        var invoker = Substitute.For<ISubscribeInvoker>();
        invoker
            .InvokeAsync(Arg.Any<ConsumerContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ConsumerExecutedResult(null, null, "msg-1", null, null)));

        var (executor, cbMock) = _CreateExecutor(invoker, storage);

        // when
        await executor.ExecuteAsync(_CreateMediumMessage(), _EmptyScope, _CreateDescriptor(), AbortToken);

        // then — both DB persistence and circuit breaker reporting happen
        await storage
            .Received(1)
            .ChangeReceiveRetryStateAsync(
                Arg.Any<MediumMessage>(),
                StatusName.Succeeded,
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            );
        await cbMock.Received(1).ReportSuccessAsync(_CircuitBreakerGroupName, CancellationToken.None);
    }
}

// Supporting types

public sealed record CbTestMessage(string Id);

public sealed class CbTestConsumer : IConsume<CbTestMessage>
{
    public ValueTask ConsumeAsync(ConsumeContext<CbTestMessage> context, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}
