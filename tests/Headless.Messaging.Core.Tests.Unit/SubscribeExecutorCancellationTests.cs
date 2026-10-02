// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.CircuitBreaker;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Messaging.RequestReply;
using Headless.Messaging.Retry;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tests.Helpers;

namespace Tests;

/// <summary>
/// Tests that <see cref="SubscribeExecutor"/> correctly distinguishes between handler-timeout
/// cancellations (TaskCanceledException where IsCancellationRequested = false) and
/// app-shutdown cancellations (OperationCanceledException where IsCancellationRequested = true).
/// </summary>
public sealed class SubscribeExecutorCancellationTests : TestBase
{
    private static readonly IServiceProvider _EmptyScope = new ServiceCollection().BuildServiceProvider();

    private static MediumMessage _CreateMediumMessage(string messageName = "test.messageName")
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = Guid.NewGuid().ToString(),
            [Headers.MessageName] = messageName,
        };

        return new MediumMessage
        {
            StorageId = Guid.NewGuid(),
            Origin = new Message(headers, "{}"),
            Content = "{}",
            Lane = MessageLane.Bus,
            Added = DateTimeOffset.UtcNow,
        };
    }

    private static ConsumerExecutorDescriptor _CreateDescriptor()
    {
        return new ConsumerExecutorDescriptor
        {
            Lane = MessageLane.Bus,
            ConsumerType = typeof(CancellationExecutorTestConsumer),
            MessageType = typeof(CancellationExecutorTestMessage),
            MessageName = "test.messageName",
            SubscriptionName = "test",
        };
    }

    private SubscribeExecutor _CreateExecutor(
        ISubscribeInvoker invoker,
        IDataStorage storage,
        MessagingOptions? messagingOptions = null,
        ICircuitBreakerStateManager? circuitBreaker = null
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
                Arg.Any<MessageContentWrite>(),
                Arg.Any<RetryDelay?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(true));

        var services = new ServiceCollection();
        services.AddLogging();
        services.ConfigureMessaging(messaging =>
            messaging.Message<CancellationExecutorTestMessage>("test.messageName")
        );
        services.AddHeadlessMessaging(setup =>
        {
            setup.AddConsumer<CancellationExecutorTestConsumer>();
            setup.UseInMemory();
            setup.UseProcessLocalInMemoryStorage();
        });

        var provider = services.BuildServiceProvider();
        var logger = provider.GetRequiredService<ILogger<SubscribeExecutor>>();
        var options = Options.Create(
            messagingOptions
                ?? new MessagingOptions
                {
                    RetryPolicy =
                    {
                        RetryStrategy = TestRetryStrategies.FixedDelay(0, TimeSpan.Zero),
                        MaxPersistedRetries = 0,
                    },
                }
        );

        circuitBreaker ??= Substitute.For<ICircuitBreakerStateManager>();
        return new SubscribeExecutor(provider, storage, invoker, TimeProvider.System, logger, options, circuitBreaker);
    }

    [Fact]
    public async Task should_propagate_as_failed_when_task_canceled_exception_without_requested_token()
    {
        // given — simulate HttpClient / downstream timeout:
        //   TaskCanceledException where CancellationToken.IsCancellationRequested = false
        var storage = Substitute.For<IDataStorage>();
        storage
            .ChangeReceiveStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<MessageContentWrite>(),
                Arg.Any<RetryDelay?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>()
            )
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
        await storage
            .Received()
            .ChangeReceiveRetryStateAsync(
                Arg.Any<MediumMessage>(),
                StatusName.Failed,
                Arg.Any<MessageContentWrite>(),
                Arg.Any<RetryDelay?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_not_write_state_when_operation_canceled_exception_with_requested_token()
    {
        // given — simulate app-shutdown cancellation:
        //   OperationCanceledException where CancellationToken.IsCancellationRequested = true.
        // The executor must classify this as cancellation, NOT invoke
        // OnExhausted, and NOT write a state transition. The row keeps its prior NextRetryAt and
        // the persisted retry processor picks it up on restart.
        var storage = Substitute.For<IDataStorage>();
        storage
            .ChangeReceiveStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<MessageContentWrite>(),
                Arg.Any<RetryDelay?>(),
                cancellationToken: Arg.Any<CancellationToken>()
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
                    RetryStrategy = TestRetryStrategies.FixedDelay(0, TimeSpan.Zero),
                    MaxPersistedRetries = 0,
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
        var executionState = new RetryExecutionState();
        var result = await executor.ExecuteRetryAsync(message, _EmptyScope, executionState, descriptor, cts.Token);

        // then — Shutdown OCE: no state write, no OnExhausted. Row keeps prior NextRetryAt/Status.
        result.Succeeded.Should().BeFalse();
        executionState.LeaseClearedByTransition.Should().BeFalse();
        message.Retries.Should().Be(0);
        callbackInvoked.Should().BeFalse();
        await storage
            .DidNotReceive()
            .ChangeReceiveStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<MessageContentWrite>(),
                Arg.Any<RetryDelay?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_leave_the_row_untouched_when_the_requester_stops_while_the_consumer_awaits_a_request()
    {
        // given — a consumer awaiting its own request without passing a token, so only the requester stopping ends it
        var storage = Substitute.For<IDataStorage>();
        var circuitBreaker = Substitute.For<ICircuitBreakerStateManager>();
        var pending = new PendingRequests();
        var call = new PendingRequest(
            "outbound-request",
            typeof(string),
            "outbound.response",
            "1",
            TimeSpan.FromMinutes(1),
            TimeProvider.System
        );
        pending.TryRegister(call, TimeSpan.FromMinutes(1), CancellationToken.None).Should().BeTrue();

        var awaiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var invoker = Substitute.For<ISubscribeInvoker>();
        invoker
            .InvokeAsync(Arg.Any<ConsumerContext>(), Arg.Any<CancellationToken>())
            .Returns<Task<ConsumerExecutedResult>>(async _ =>
            {
                awaiting.TrySetResult();
                await call.Outcome;
                return new ConsumerExecutedResult(null, null, "message-id", null, null);
            });

        var callbackInvoked = false;
        var executor = _CreateExecutor(
            invoker,
            storage,
            _ExhaustingOptions(() => callbackInvoked = true),
            circuitBreaker
        );
        using var dispatch = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        var execution = executor.ExecuteAsync(_CreateMediumMessage(), _EmptyScope, _CreateDescriptor(), dispatch.Token);
        await awaiting.Task.WaitAsync(AbortToken);

        // when — the host quiesces in bootstrapper order: the dispatcher cancels its token, then the reply listener
        // fails every pending call before the cancellation callbacks have run
        var dispatcherQuiesced = dispatch.CancelAsync();
        pending.Close();
        await dispatcherQuiesced;
        await _CompleteAsShutdownAsync(execution);

        // then — host shutdown, not a consumer failure: the row stays for redelivery
        callbackInvoked.Should().BeFalse();
        await _AssertNoStateWriteAsync(storage);
        await circuitBreaker
            .DidNotReceive()
            .ReportFailureAsync(Arg.Any<string>(), Arg.Any<Exception>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_leave_the_row_untouched_when_a_request_is_refused_because_the_requester_is_stopping()
    {
        // given — the consumer's request is refused because the requester began to stop, during host shutdown
        var storage = Substitute.For<IDataStorage>();
        var circuitBreaker = Substitute.For<ICircuitBreakerStateManager>();
        using var dispatch = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        var invoker = Substitute.For<ISubscribeInvoker>();
        invoker
            .InvokeAsync(Arg.Any<ConsumerContext>(), Arg.Any<CancellationToken>())
            .Returns<Task<ConsumerExecutedResult>>(async _ =>
            {
                await dispatch.CancelAsync();
                throw ReplyListenerHost.Stopping("outbound-request");
            });

        var callbackInvoked = false;
        var executor = _CreateExecutor(
            invoker,
            storage,
            _ExhaustingOptions(() => callbackInvoked = true),
            circuitBreaker
        );

        // when
        await _CompleteAsShutdownAsync(
            executor.ExecuteAsync(_CreateMediumMessage(), _EmptyScope, _CreateDescriptor(), dispatch.Token)
        );

        // then
        callbackInvoked.Should().BeFalse();
        await _AssertNoStateWriteAsync(storage);
        await circuitBreaker
            .DidNotReceive()
            .ReportFailureAsync(Arg.Any<string>(), Arg.Any<Exception>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_record_a_failure_when_a_request_is_not_sent_for_a_reason_other_than_the_requester_stopping()
    {
        // given — the host is stopping, but the request failed for its own reason
        var storage = Substitute.For<IDataStorage>();
        using var dispatch = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        var invoker = Substitute.For<ISubscribeInvoker>();
        invoker
            .InvokeAsync(Arg.Any<ConsumerContext>(), Arg.Any<CancellationToken>())
            .Returns<Task<ConsumerExecutedResult>>(async _ =>
            {
                await dispatch.CancelAsync();
                throw new RequestNotSentException("Publish middleware suppressed the request, so it was not sent.");
            });

        var executor = _CreateExecutor(invoker, storage, _ExhaustingOptions(() => { }));

        // when
        await _CompleteAsShutdownAsync(
            executor.ExecuteAsync(_CreateMediumMessage(), _EmptyScope, _CreateDescriptor(), dispatch.Token)
        );

        // then
        await storage
            .Received()
            .ChangeReceiveRetryStateAsync(
                Arg.Any<MediumMessage>(),
                StatusName.Failed,
                Arg.Any<MessageContentWrite>(),
                Arg.Any<RetryDelay?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            );
    }

    private static MessagingOptions _ExhaustingOptions(Action onExhausted)
    {
        return new MessagingOptions
        {
            RetryPolicy =
            {
                RetryStrategy = TestRetryStrategies.FixedDelay(0, TimeSpan.Zero),
                MaxPersistedRetries = 0,
                OnExhausted = (_, _) =>
                {
                    onExhausted();
                    return Task.CompletedTask;
                },
            },
        };
    }

    // A dispatch whose token the host canceled ends either with a failed result or with OperationCanceledException,
    // and the dispatcher reads both as shutdown; what matters is what the attempt wrote and reported on the way out.
    private static async Task _CompleteAsShutdownAsync(Task<OperateResult> execution)
    {
        try
        {
            var result = await execution.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);
            result.Succeeded.Should().BeFalse();
        }
        catch (OperationCanceledException) when (!AbortToken.IsCancellationRequested) { }
    }

    private static async Task _AssertNoStateWriteAsync(IDataStorage storage)
    {
        await storage
            .DidNotReceive()
            .ChangeReceiveRetryStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<MessageContentWrite>(),
                Arg.Any<RetryDelay?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_not_write_state_when_task_canceled_exception_with_requested_token()
    {
        // given — TaskCanceledException but the token IS requested (handler respected shutdown CT).
        // X4 invariant: shutdown-classified cancellations leave the row untouched so the persisted
        // retry processor picks it up on restart.
        var storage = Substitute.For<IDataStorage>();
        storage
            .ChangeReceiveStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<MessageContentWrite>(),
                Arg.Any<RetryDelay?>(),
                cancellationToken: Arg.Any<CancellationToken>()
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
                Arg.Any<MessageContentWrite>(),
                Arg.Any<RetryDelay?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>()
            );
    }
}

// Supporting types

public sealed record CancellationExecutorTestMessage(string Id);

[BusConsumer(Identity)]
public sealed class CancellationExecutorTestConsumer : IConsume<CancellationExecutorTestMessage>
{
    public const string Identity = "tests.subscribe-executor";

    public ValueTask ConsumeAsync(
        ConsumeContext<CancellationExecutorTestMessage> context,
        CancellationToken cancellationToken
    )
    {
        return ValueTask.CompletedTask;
    }
}
