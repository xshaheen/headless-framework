// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Messaging.Persistence;
using Headless.Messaging.Retry;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Tests.Helpers;

namespace Tests;

/// <summary>
/// A consume attempt slides its lease forward while the handler runs, and an attempt whose lease is lost stops and
/// writes no state, because another node may own the row by then.
/// </summary>
public sealed class SubscribeExecutorLeaseRenewalTests : TestBase
{
    private static readonly TimeSpan _DispatchTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan _RenewalInterval = TimeSpan.FromSeconds(10);
    private static readonly IServiceProvider _EmptyScope = new ServiceCollection().BuildServiceProvider();

    private readonly FakeTimeProvider _clock = new();
    private readonly IDataStorage _storage = Substitute.For<IDataStorage>();
    private readonly ISubscribeInvoker _invoker = Substitute.For<ISubscribeInvoker>();
    private int _renewals;

    [Fact]
    public async Task should_renew_the_lease_on_its_cadence_while_a_long_handler_runs()
    {
        // given: a handler that runs for three renewal intervals
        _RenewalsReturn(true);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _invoker
            .InvokeAsync(Arg.Any<ConsumerContext>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                await release.Task;
                return new ConsumerExecutedResult(null, null, "message-id", null, null);
            });
        var executor = _CreateExecutor();

        // when
        var execution = executor.ExecuteAsync(_CreateMessage(), _EmptyScope, _CreateDescriptor(), AbortToken);
        await _AdvanceUntilAsync(() => Volatile.Read(ref _renewals) >= 3);
        release.SetResult();
        var result = await execution;

        // then: each renewal asked for a full lease, and the attempt still wrote its success
        result.Succeeded.Should().BeTrue();
        await _storage
            .Received()
            .RenewReceiveLeaseAsync(Arg.Any<MediumMessage>(), _DispatchTimeout, Arg.Any<CancellationToken>());
        await _storage
            .Received(1)
            .ChangeReceiveRetryStateAsync(
                Arg.Any<MediumMessage>(),
                StatusName.Succeeded,
                Arg.Any<MessageContentWrite>(),
                Arg.Any<RetryDelay?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_cancel_the_handler_and_write_no_state_when_the_lease_is_lost()
    {
        // given: the first renewal finds the lease gone
        _RenewalsReturn(false);
        var handlerCancelled = false;
        _invoker
            .InvokeAsync(Arg.Any<ConsumerContext>(), Arg.Any<CancellationToken>())
            .Returns<Task<ConsumerExecutedResult>>(async call =>
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, call.Arg<CancellationToken>());
                }
                catch (OperationCanceledException)
                {
                    handlerCancelled = true;
                    throw;
                }

                return new ConsumerExecutedResult(null, null, "message-id", null, null);
            });
        var executor = _CreateExecutor();

        // when
        var execution = executor.ExecuteAsync(_CreateMessage(), _EmptyScope, _CreateDescriptor(), AbortToken);
        await _AdvanceUntilAsync(() => execution.IsCompleted);
        var result = await execution;

        // then: the handler saw its token cancelled, and nothing was written to the row another node may own
        handlerCancelled.Should().BeTrue();
        result.Succeeded.Should().BeFalse();
        result.Exception.Should().BeAssignableTo<OperationCanceledException>();
        _AssertNoStateWritten();
    }

    [Fact]
    public async Task should_write_no_success_when_a_handler_ignores_its_token_and_finishes_after_the_lease_is_lost()
    {
        // given: a handler that never looks at its token
        _RenewalsReturn(false);
        var renewed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _storage
            .RenewReceiveLeaseAsync(Arg.Any<MediumMessage>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                renewed.TrySetResult();
                return ValueTask.FromResult(false);
            });
        _invoker
            .InvokeAsync(Arg.Any<ConsumerContext>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                await renewed.Task;
                return new ConsumerExecutedResult(null, null, "message-id", null, null);
            });
        var executor = _CreateExecutor();

        // when
        var execution = executor.ExecuteAsync(_CreateMessage(), _EmptyScope, _CreateDescriptor(), AbortToken);
        await _AdvanceUntilAsync(() => execution.IsCompleted);
        var result = await execution;

        // then
        result.Succeeded.Should().BeFalse();
        _AssertNoStateWritten();
    }

    [Fact]
    public async Task should_not_renew_when_the_handler_finishes_within_one_interval()
    {
        // given
        _RenewalsReturn(true);
        _invoker
            .InvokeAsync(Arg.Any<ConsumerContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ConsumerExecutedResult(null, null, "message-id", null, null)));
        var executor = _CreateExecutor();

        // when
        var result = await executor.ExecuteAsync(_CreateMessage(), _EmptyScope, _CreateDescriptor(), AbortToken);
        _clock.Advance(_RenewalInterval * 3);

        // then
        result.Succeeded.Should().BeTrue();
        Volatile.Read(ref _renewals).Should().Be(0);
    }

    private void _RenewalsReturn(bool renewed)
    {
        _storage
            .RenewReceiveLeaseAsync(Arg.Any<MediumMessage>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Interlocked.Increment(ref _renewals);
                return ValueTask.FromResult(renewed);
            });
    }

    /// <summary>Steps the renewal cadence until <paramref name="condition"/> holds; the loop may still be starting.</summary>
    private async Task _AdvanceUntilAsync(Func<bool> condition)
    {
        for (var step = 0; step < 1_000 && !condition(); step++)
        {
            _clock.Advance(_RenewalInterval);
            await Task.Delay(TimeSpan.FromMilliseconds(2), AbortToken);
        }

        condition().Should().BeTrue();
    }

    private void _AssertNoStateWritten()
    {
        _storage
            .ReceivedCalls()
            .Select(call => call.GetMethodInfo().Name)
            .Should()
            .NotContain(name => name.StartsWith("Change", StringComparison.Ordinal));
    }

    private SubscribeExecutor _CreateExecutor()
    {
        _storage
            .LeaseReceiveAndReserveAttemptAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(true));
        _storage
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
        var options = Options.Create(
            new MessagingOptions
            {
                RetryPolicy =
                {
                    RetryStrategy = TestRetryStrategies.FixedDelay(0, TimeSpan.Zero),
                    MaxPersistedRetries = 0,
                    DispatchTimeout = _DispatchTimeout,
                    LeaseRenewalInterval = _RenewalInterval,
                },
            }
        );

        return new SubscribeExecutor(
            provider,
            _storage,
            _invoker,
            _clock,
            provider.GetRequiredService<ILogger<SubscribeExecutor>>(),
            options,
            Substitute.For<ICircuitBreakerStateManager>()
        );
    }

    private static MediumMessage _CreateMessage()
    {
        return new MediumMessage
        {
            StorageId = Guid.NewGuid(),
            Origin = new Message(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    [Headers.MessageId] = Guid.NewGuid().ToString(),
                    [Headers.MessageName] = "test.messageName",
                },
                "{}"
            ),
            Content = "{}",
            Lane = MessageLane.Bus,
            Added = DateTimeOffset.UtcNow,
        };
    }

    private static ConsumerExecutorDescriptor _CreateDescriptor()
    {
        return new ConsumerExecutorDescriptor
        {
            FailurePolicy = MessagingOptions.FrameworkDefaultFailurePolicy,
            Lane = MessageLane.Bus,
            ConsumerType = typeof(CancellationExecutorTestConsumer),
            MessageType = typeof(CancellationExecutorTestMessage),
            MessageName = "test.messageName",
            SubscriptionName = "test",
        };
    }
}
