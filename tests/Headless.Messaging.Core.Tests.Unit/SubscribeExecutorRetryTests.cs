// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Exceptions;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Messaging.Retry;
using Headless.Reliability;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tests.Helpers;

#pragma warning disable MA0015 // Specify the parameter name in ArgumentException
namespace Tests;

public sealed class SubscribeExecutorRetryTests : TestBase
{
    private static readonly IServiceProvider _EmptyScope = new ServiceCollection().BuildServiceProvider();

    private static MediumMessage _CreateMediumMessage()
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = Guid.NewGuid().ToString(),
            [Headers.MessageName] = "test.messageName",
            [Headers.ConsumerIdentity] = CancellationExecutorTestConsumer.Identity,
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

    private static ConsumerExecutorDescriptor _CreateDescriptor(FailurePolicyDefinition? failurePolicy = null)
    {
        return new ConsumerExecutorDescriptor
        {
            FailurePolicy = failurePolicy ?? MessagingOptions.FrameworkDefaultFailurePolicy,
            Lane = MessageLane.Bus,
            ConsumerType = typeof(CancellationExecutorTestConsumer),
            MessageType = typeof(CancellationExecutorTestMessage),
            MessageName = "test.messageName",
            SubscriptionName = "test-group",
            ConsumerIdentity = CancellationExecutorTestConsumer.Identity,
            MessageContractVersion = "1",
        };
    }

    private static SubscribeExecutor _CreateExecutor(
        ISubscribeInvoker invoker,
        IDataStorage storage,
        MessagingOptions options
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

        return new SubscribeExecutor(provider, storage, invoker, TimeProvider.System, logger, Options.Create(options));
    }

    [Fact]
    public async Task persisted_inbox_retry_should_resolve_by_inbox_key_not_by_the_identity_header()
    {
        var storage = Substitute.For<IDataStorage>();
        var invoker = Substitute.For<ISubscribeInvoker>();
        invoker
            .InvokeAsync(Arg.Any<ConsumerContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ConsumerExecutedResult(null, null, null!, null, null)));
        var executor = _CreateExecutor(
            invoker,
            storage,
            new MessagingOptions { RequiredInboxCapability = MessagingInboxCapabilityTier.DurableDedupeOnly }
        );
        var message = _CreateMediumMessage();
        message.Origin.Headers[Headers.ConsumerIdentity] = "obsolete.consumer";
        message.InboxKey = new InboxKey(
            TenantId: null,
            message.Origin.Id,
            MessageLane.Bus,
            "test.messageName",
            "1",
            CancellationExecutorTestConsumer.Identity,
            Generation: 0
        );

        var result = await executor.ExecuteAsync(message, _EmptyScope, descriptor: null, AbortToken);

        result.Succeeded.Should().BeTrue();
        await invoker
            .Received(1)
            .InvokeAsync(
                Arg.Is<ConsumerContext>(context =>
                    context.ConsumerDescriptor.ConsumerIdentity == CancellationExecutorTestConsumer.Identity
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task transactional_inbox_should_share_attempt_scope_with_runner_and_invoker()
    {
        var storage = Substitute.For<IDataStorage>();
        var invoker = Substitute.For<ISubscribeInvoker>();
        invoker
            .InvokeInScopeAsync(Arg.Any<ConsumerContext>(), Arg.Any<IServiceProvider>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ConsumerExecutedResult(null, null, null!, null, null)));
        var executor = _CreateExecutor(invoker, storage, new MessagingOptions());
        var message = _CreateMediumMessage();
        message.InboxKey = new InboxKey(
            TenantId: null,
            message.Origin.Id,
            MessageLane.Bus,
            "test.messageName",
            "v1",
            CancellationExecutorTestConsumer.Identity,
            Generation: 0
        );
        var runner = Substitute.For<IInboxTransactionRunner>();
        runner
            .ExecuteAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<Func<IUnitOfWork, CancellationToken, Task>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
                call.ArgAt<Func<IUnitOfWork, CancellationToken, Task>>(1)(
                    Substitute.For<IUnitOfWork>(),
                    call.ArgAt<CancellationToken>(2)
                )
            );
        IServiceProvider? runnerServices = null;
        await using var dispatchServices = new ServiceCollection()
            .AddScoped<IInboxTransactionRunner>(services =>
            {
                runnerServices = services;
                return runner;
            })
            .BuildServiceProvider();

        var result = await executor.ExecuteAsync(message, dispatchServices, _CreateDescriptor(), AbortToken);

        result.Succeeded.Should().BeTrue();
        await runner
            .Received(1)
            .ExecuteAsync(message, Arg.Any<Func<IUnitOfWork, CancellationToken, Task>>(), Arg.Any<CancellationToken>());
        await invoker
            .Received(1)
            .InvokeInScopeAsync(
                Arg.Any<ConsumerContext>(),
                Arg.Is<IServiceProvider>(services =>
                    ReferenceEquals(services, runnerServices) && !ReferenceEquals(services, dispatchServices)
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task stale_transactional_inbox_fence_should_wait_for_recovery_without_inline_reentry()
    {
        var storage = Substitute.For<IDataStorage>();
        var invoker = Substitute.For<ISubscribeInvoker>();
        var executor = _CreateExecutor(invoker, storage, new MessagingOptions());
        var message = _CreateMediumMessage();
        message.InboxKey = new InboxKey(
            TenantId: null,
            message.Origin.Id,
            MessageLane.Bus,
            "test.messageName",
            "v1",
            CancellationExecutorTestConsumer.Identity,
            Generation: 0
        );
        var runner = Substitute.For<IInboxTransactionRunner>();
        runner
            .ExecuteAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<Func<IUnitOfWork, CancellationToken, Task>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ => throw new StaleInboxAttemptException(message.StorageId));

        await using var dispatchServices = new ServiceCollection().AddSingleton(runner).BuildServiceProvider();

        var result = await executor.ExecuteAsync(message, dispatchServices, _CreateDescriptor(), AbortToken);

        result.Succeeded.Should().BeFalse();
        await runner
            .Received(1)
            .ExecuteAsync(message, Arg.Any<Func<IUnitOfWork, CancellationToken, Task>>(), Arg.Any<CancellationToken>());
        await invoker.DidNotReceive().InvokeAsync(Arg.Any<ConsumerContext>(), Arg.Any<CancellationToken>());
    }

    // ─── Additive failure-policy budget ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task should_invoke_handler_one_plus_immediate_plus_delayed_times_before_the_row_is_terminal()
    {
        // given — 2 immediate and 3 delayed retries: 1 + 2 + 3 = 6 attempts, never (2 + 1) × (3 + 1).
        var storage = Substitute.For<IDataStorage>();
        var invoker = _AlwaysThrowing(new TimeoutException("boom"), out var invocations);
        var exhausted = 0;
        var executor = _CreateExecutor(invoker, storage, _OptionsCountingExhausted(() => exhausted++));
        var writes = _CaptureWrites(storage);
        var descriptor = _CreateDescriptor(_Policy(immediate: 2, delayed: 3));
        var message = _CreateMediumMessage();

        // when — the first dispatch, then one dispatch per delayed pickup, until the row turns terminal.
        var dispatches = await _DispatchUntilTerminalAsync(executor, message, descriptor, writes, maxDispatches: 10);

        // then
        invocations().Should().Be(6);
        dispatches.Should().Be(4, "the first dispatch plus one pickup per delayed retry");
        writes.Count(_IsTerminal).Should().Be(1);
        writes[^1].Should().Match<StateWrite>(write => _IsTerminal(write) && write.OriginalRetries == 3);
        exhausted.Should().Be(1);
    }

    [Fact]
    public async Task should_run_immediate_retries_in_one_dispatch_without_scheduling_a_delay_between_them()
    {
        // given — the strategy configured for publishing would sleep 5 seconds per retry; consuming must ignore it.
        var storage = Substitute.For<IDataStorage>();
        var invoker = _AlwaysThrowing(new TimeoutException("boom"), out var invocations);
        var options = new MessagingOptions
        {
            RetryPolicy =
            {
                RetryStrategy = TestRetryStrategies.FixedDelay(2, TimeSpan.FromSeconds(5)),
                InitialDispatchGrace = TimeSpan.FromSeconds(7),
            },
        };
        var executor = _CreateExecutor(invoker, storage, options);
        var writes = _CaptureWrites(storage);
        var descriptor = _CreateDescriptor(_Policy(immediate: 2, delayed: 1));
        var message = _CreateMediumMessage();
        var stopwatch = Stopwatch.StartNew();

        // when
        await executor.ExecuteAsync(message, _EmptyScope, descriptor, AbortToken);

        // then — three attempts in one call, the in-flight writes only pad crash recovery by the grace,
        // and the single delayed retry is scheduled once the immediate retries are spent.
        stopwatch.Stop();
        invocations().Should().Be(3);
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(4));
        writes.Should().HaveCount(3);
        writes
            .Take(2)
            .Should()
            .OnlyContain(write =>
                write.Status == StatusName.Scheduled
                && write.Delay != null
                && write.Delay.Value.KeepsLaterDue
                && write.Delay.Value.Delay == options.RetryPolicy.InitialDispatchGrace
            );
        writes[2]
            .Should()
            .Match<StateWrite>(write =>
                write.Status == StatusName.Failed
                && write.Delay != null
                && !write.Delay.Value.KeepsLaterDue
                && write.OriginalRetries == 0
            );
        message.Retries.Should().Be(1);
        message.InlineAttempts.Should().Be(0);
    }

    [Fact]
    public async Task should_schedule_each_delayed_retry_with_a_delay_growing_with_the_attempt_number_up_to_the_cap()
    {
        // given — 30 s doubling, capped at 100 s: base delays 30, 60, 100, 100.
        var initial = TimeSpan.FromSeconds(30);
        var cap = TimeSpan.FromSeconds(100);
        var storage = Substitute.For<IDataStorage>();
        var invoker = _AlwaysThrowing(new TimeoutException("boom"), out var invocations);
        var executor = _CreateExecutor(invoker, storage, new MessagingOptions());
        var writes = _CaptureWrites(storage);
        var descriptor = _CreateDescriptor(_Policy(immediate: 0, delayed: 4, initial, cap));
        var message = _CreateMediumMessage();

        // when
        await _DispatchUntilTerminalAsync(executor, message, descriptor, writes, maxDispatches: 10);

        // then — one Exactly delay per delayed retry, each inside its jitter band, then the terminal write.
        invocations().Should().Be(5);
        var scheduled = writes.Where(write => write.Delay is { KeepsLaterDue: false }).ToList();
        scheduled.Should().HaveCount(4);
        for (var n = 1; n <= 4; n++)
        {
            var (lower, upper) = _JitterBand(initial, cap, n);
            scheduled[n - 1].Delay!.Value.Delay.Should().BeGreaterThanOrEqualTo(lower).And.BeLessThanOrEqualTo(upper);
            scheduled[n - 1].OriginalRetries.Should().Be(n - 1);
        }

        scheduled[1].Delay!.Value.Delay.Should().BeGreaterThan(scheduled[0].Delay!.Value.Delay);
        scheduled[2].Delay!.Value.Delay.Should().BeGreaterThan(scheduled[1].Delay!.Value.Delay);
        writes[^1].Should().Match<StateWrite>(write => _IsTerminal(write));
    }

    [Fact]
    public async Task should_run_one_attempt_without_immediate_retries_on_a_delayed_pickup()
    {
        // given — a row the retry processor picked up for its first delayed retry.
        var initial = TimeSpan.FromSeconds(10);
        var cap = TimeSpan.FromMinutes(10);
        var storage = Substitute.For<IDataStorage>();
        var invoker = _AlwaysThrowing(new TimeoutException("boom"), out var invocations);
        var executor = _CreateExecutor(invoker, storage, new MessagingOptions());
        var writes = _CaptureWrites(storage);
        var message = _CreateMediumMessage();
        message.Retries = 1;

        // when
        await executor.ExecuteRetryAsync(
            message,
            _EmptyScope,
            new RetryExecutionState(),
            _CreateDescriptor(_Policy(immediate: 2, delayed: 3, initial, cap)),
            AbortToken
        );

        // then — one attempt, then delayed retry 2 is scheduled.
        invocations().Should().Be(1);
        writes.Should().ContainSingle();
        var (lower, upper) = _JitterBand(initial, cap, 2);
        writes[0].Status.Should().Be(StatusName.Failed);
        writes[0].Delay!.Value.KeepsLaterDue.Should().BeFalse();
        writes[0].Delay!.Value.Delay.Should().BeGreaterThanOrEqualTo(lower).And.BeLessThanOrEqualTo(upper);
        message.Retries.Should().Be(2);
    }

    [Fact]
    public async Task should_give_a_row_exactly_at_its_budget_its_final_attempt()
    {
        var storage = Substitute.For<IDataStorage>();
        var invoker = _AlwaysThrowing(new TimeoutException("boom"), out var invocations);
        var exhausted = 0;
        var executor = _CreateExecutor(invoker, storage, _OptionsCountingExhausted(() => exhausted++));
        var writes = _CaptureWrites(storage);
        var message = _CreateMediumMessage();
        message.Retries = 3;

        await executor.ExecuteAsync(message, _EmptyScope, _CreateDescriptor(_Policy(0, 3)), AbortToken);

        invocations().Should().Be(1);
        writes.Should().ContainSingle().Which.Should().Match<StateWrite>(write => _IsTerminal(write));
        exhausted.Should().Be(1);
    }

    [Fact]
    public async Task should_claim_and_run_a_consumer_budget_larger_than_the_publish_cap()
    {
        // given — 20 delayed retries, while the host-wide (publish-only) cap stays at its default of 15.
        var options = new MessagingOptions();
        options.RetryPolicy.MaxPersistedRetries.Should().BeLessThan(16);
        var storage = Substitute.For<IDataStorage>();
        var invoker = _AlwaysThrowing(new TimeoutException("boom"), out var invocations);
        var executor = _CreateExecutor(invoker, storage, options);
        var writes = _CaptureWrites(storage);
        var message = _CreateMediumMessage();
        message.Retries = 16;

        // when
        await executor.ExecuteAsync(message, _EmptyScope, _CreateDescriptor(_Policy(0, 20)), AbortToken);

        // then — the handler runs and delayed retry 17 is scheduled.
        invocations().Should().Be(1);
        writes.Should().ContainSingle().Which.Delay.Should().NotBeNull();
        message.Retries.Should().Be(17);
    }

    // ─── Terminal failures and OnExhausted ──────────────────────────────────────────────────────────

    [Fact]
    public async Task should_fail_after_one_attempt_and_fire_on_exhausted_once_when_a_fail_rule_matches()
    {
        var storage = Substitute.For<IDataStorage>();
        var invoker = _AlwaysThrowing(new InvalidOperationException("declined"), out var invocations);
        var exhausted = new List<FailedInfo>();
        var executor = _CreateExecutor(invoker, storage, _OptionsCountingExhausted(exhausted.Add));
        var writes = _CaptureWrites(storage);
        var message = _CreateMediumMessage();
        var policy = new FailurePolicyBuilder()
            .Immediate(2)
            .Delayed(3, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10))
            .FailOn<InvalidOperationException>()
            .Build();

        var result = await executor.ExecuteAsync(message, _EmptyScope, _CreateDescriptor(policy), AbortToken);

        result.Succeeded.Should().BeFalse();
        invocations().Should().Be(1);
        writes.Should().ContainSingle().Which.Should().Match<StateWrite>(write => _IsTerminal(write));
        message.Retries.Should().Be(0, "a matched fail rule skips the remaining retries");
        exhausted.Should().ContainSingle();
        exhausted[0].Exception.GetBaseException().Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task should_fail_at_once_on_argument_exception_even_when_the_policy_has_no_fail_rule()
    {
        var storage = Substitute.For<IDataStorage>();
        var invoker = _AlwaysThrowing(new ArgumentException("invalid param"), out var invocations);
        var exhausted = 0;
        var executor = _CreateExecutor(invoker, storage, _OptionsCountingExhausted(() => exhausted++));
        var writes = _CaptureWrites(storage);
        var message = _CreateMediumMessage();

        await executor.ExecuteAsync(message, _EmptyScope, _CreateDescriptor(_Policy(3, 5)), AbortToken);

        invocations().Should().Be(1);
        writes.Should().ContainSingle().Which.Should().Match<StateWrite>(write => _IsTerminal(write));
        message.Retries.Should().Be(0);
        exhausted.Should().Be(1);
    }

    [Fact]
    public async Task should_treat_a_throwing_fail_rule_as_matched()
    {
        var storage = Substitute.For<IDataStorage>();
        var invoker = _AlwaysThrowing(new TimeoutException("boom"), out var invocations);
        var exhausted = 0;
        var executor = _CreateExecutor(invoker, storage, _OptionsCountingExhausted(() => exhausted++));
        var writes = _CaptureWrites(storage);
        var policy = new FailurePolicyBuilder()
            .Immediate(2)
            .FailWhen(static _ => throw new InvalidOperationException("broken rule"))
            .Build();

        await executor.ExecuteAsync(_CreateMediumMessage(), _EmptyScope, _CreateDescriptor(policy), AbortToken);

        invocations().Should().Be(1);
        writes.Should().ContainSingle().Which.Should().Match<StateWrite>(write => _IsTerminal(write));
        exhausted.Should().Be(1);
    }

    [Fact]
    public async Task should_show_fail_rules_the_handler_exception_unwrapped()
    {
        var storage = Substitute.For<IDataStorage>();
        var invoker = _AlwaysThrowing(new TimeoutException("boom"), out _);
        var executor = _CreateExecutor(invoker, storage, new MessagingOptions());
        _CaptureWrites(storage);
        var seen = new List<Type>();
        var policy = new FailurePolicyBuilder()
            .FailWhen(exception =>
            {
                seen.Add(exception.GetType());
                return false;
            })
            .Build();

        await executor.ExecuteAsync(_CreateMediumMessage(), _EmptyScope, _CreateDescriptor(policy), AbortToken);

        seen.Should().Equal(typeof(TimeoutException));
    }

    [Fact]
    public async Task should_ignore_the_publish_retry_strategy_when_consuming()
    {
        // given — the publish strategy would allow five inline retries; the consumer's policy allows none.
        var storage = Substitute.For<IDataStorage>();
        var invoker = _AlwaysThrowing(new TimeoutException("boom"), out var invocations);
        var options = new MessagingOptions
        {
            RetryPolicy = { RetryStrategy = TestRetryStrategies.ZeroDelay(5), MaxPersistedRetries = 5 },
        };
        var executor = _CreateExecutor(invoker, storage, options);
        var writes = _CaptureWrites(storage);

        await executor.ExecuteAsync(
            _CreateMediumMessage(),
            _EmptyScope,
            _CreateDescriptor(FailurePolicyDefinition.None),
            AbortToken
        );

        invocations().Should().Be(1);
        writes.Should().ContainSingle().Which.Should().Match<StateWrite>(write => _IsTerminal(write));
    }

    [Fact]
    public async Task should_treat_message_deserialization_exception_as_terminal_and_fire_on_exhausted_once()
    {
        var storage = Substitute.For<IDataStorage>();
        var invoker = _AlwaysThrowing(
            new MessageDeserializationException("Stage B payload deserialization failed"),
            out var invocations
        );
        var exhausted = new List<FailedInfo>();
        var executor = _CreateExecutor(invoker, storage, _OptionsCountingExhausted(exhausted.Add));
        var writes = _CaptureWrites(storage);
        var message = _CreateMediumMessage();

        var result = await executor.ExecuteAsync(message, _EmptyScope, _CreateDescriptor(_Policy(5, 5)), AbortToken);

        result.Succeeded.Should().BeFalse();
        result.Exception.Should().BeOfType<SubscriberExecutionFailedException>();
        result.Exception!.InnerException.Should().BeOfType<MessageDeserializationException>();
        invocations().Should().Be(1);
        message.Retries.Should().Be(0);
        writes
            .Should()
            .ContainSingle()
            .Which.Should()
            .Match<StateWrite>(write => _IsTerminal(write) && write.LockedUntil == null);
        exhausted.Should().ContainSingle();
        exhausted[0].Exception.GetBaseException().Should().BeOfType<MessageDeserializationException>();
    }

    [Fact]
    public async Task should_treat_wrapped_subscriber_execution_failed_exception_with_deserialization_inner_as_terminal()
    {
        var storage = Substitute.For<IDataStorage>();
        var wrapped = new SubscriberExecutionFailedException(
            "Invocation failed",
            new MessageDeserializationException("Corrupted element")
        );
        var invoker = _AlwaysThrowing(wrapped, out var invocations);
        var exhausted = 0;
        var executor = _CreateExecutor(invoker, storage, _OptionsCountingExhausted(() => exhausted++));
        var writes = _CaptureWrites(storage);
        var message = _CreateMediumMessage();

        await executor.ExecuteAsync(message, _EmptyScope, _CreateDescriptor(_Policy(3, 5)), AbortToken);

        invocations().Should().Be(1);
        exhausted.Should().Be(1);
        writes.Should().ContainSingle().Which.Should().Match<StateWrite>(write => _IsTerminal(write));
    }

    [Fact]
    public async Task should_fail_a_row_whose_consumer_is_no_longer_registered_and_fire_on_exhausted_once()
    {
        // given — a stored row of a consumer identity this host does not register, handed over without a lease.
        var storage = Substitute.For<IDataStorage>();
        var invoker = Substitute.For<ISubscribeInvoker>();
        var exhausted = new List<FailedInfo>();
        var options = _OptionsCountingExhausted(exhausted.Add);
        options.RetryPolicy.DispatchTimeout = TimeSpan.FromSeconds(23);
        var executor = _CreateExecutor(invoker, storage, options);
        var writes = _CaptureWrites(storage);
        var message = _CreateMediumMessage();
        message.Origin.Headers[Headers.ConsumerIdentity] = "unregistered.consumer";

        // when
        var result = await executor.ExecuteAsync(message, _EmptyScope, descriptor: null, AbortToken);

        // then
        result.Succeeded.Should().BeFalse();
        await invoker.DidNotReceiveWithAnyArgs().InvokeAsync(null!, AbortToken);
        await storage.Received(1).LeaseReceiveAsync(message, TimeSpan.FromSeconds(23), Arg.Any<CancellationToken>());
        writes.Should().ContainSingle().Which.Should().Match<StateWrite>(write => _IsTerminal(write));
        exhausted.Should().ContainSingle();
        exhausted[0].Exception.Should().BeOfType<SubscriberNotFoundException>();
    }

    [Fact]
    public async Task should_not_fail_an_unregistered_consumer_row_when_another_node_holds_its_lease()
    {
        var storage = Substitute.For<IDataStorage>();
        var exhausted = 0;
        var executor = _CreateExecutor(
            Substitute.For<ISubscribeInvoker>(),
            storage,
            _OptionsCountingExhausted(() => exhausted++)
        );
        storage
            .LeaseReceiveAsync(Arg.Any<MediumMessage>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(false));
        var writes = _CaptureWrites(storage);
        var message = _CreateMediumMessage();
        message.Origin.Headers[Headers.ConsumerIdentity] = "unregistered.consumer";

        await executor.ExecuteAsync(message, _EmptyScope, descriptor: null, AbortToken);

        writes.Should().BeEmpty();
        exhausted.Should().Be(0);
    }

    [Fact]
    public async Task should_fire_on_exhausted_only_on_the_node_that_wins_the_terminal_write()
    {
        // given — two nodes run the final attempt of the same row; storage lets only the first terminal write land.
        var storage = Substitute.For<IDataStorage>();
        var exhausted = 0;
        var nodeA = _CreateExecutor(
            _AlwaysThrowing(new TimeoutException("boom"), out _),
            storage,
            _OptionsCountingExhausted(() => Interlocked.Increment(ref exhausted))
        );
        var nodeB = _CreateExecutor(
            _AlwaysThrowing(new TimeoutException("boom"), out _),
            storage,
            _OptionsCountingExhausted(() => Interlocked.Increment(ref exhausted))
        );
        var terminalWrites = 0;
        var writes = _CaptureWrites(
            storage,
            write => !_IsTerminal(write) || Interlocked.Increment(ref terminalWrites) == 1
        );
        var descriptor = _CreateDescriptor(_Policy(0, 2));
        var rowOnA = _CreateMediumMessage();
        rowOnA.Retries = 2;
        var rowOnB = _CreateMediumMessage();
        rowOnB.StorageId = rowOnA.StorageId;
        rowOnB.Retries = 2;

        // when
        await nodeA.ExecuteAsync(rowOnA, _EmptyScope, descriptor, AbortToken);
        await nodeB.ExecuteAsync(rowOnB, _EmptyScope, descriptor, AbortToken);

        // then
        writes.Count(_IsTerminal).Should().Be(2);
        exhausted.Should().Be(1);
    }

    [Fact]
    public async Task should_resolve_same_scoped_service_as_dispatch_scope_when_on_exhausted_callback()
    {
        // given — a Scoped marker service. The caller (Dispatcher) creates a scope and
        // passes its IServiceProvider; the executor must surface that SAME provider through
        // FailedInfo.ServiceProvider so OnExhausted sees the live per-message scope.
        var rootServices = new ServiceCollection();
        rootServices.AddScoped<ScopedMarker>();
        var rootProvider = rootServices.BuildServiceProvider();
        using var dispatchScope = rootProvider.CreateScope();
        var expected = dispatchScope.ServiceProvider.GetRequiredService<ScopedMarker>();

        var storage = Substitute.For<IDataStorage>();
        ScopedMarker? observed = null;
        var executor = _CreateExecutor(
            _AlwaysThrowing(new TimeoutException("boom"), out _),
            storage,
            _OptionsCountingExhausted(info => observed = info.ServiceProvider.GetRequiredService<ScopedMarker>())
        );

        // when
        await executor.ExecuteAsync(
            _CreateMediumMessage(),
            dispatchScope.ServiceProvider,
            _CreateDescriptor(FailurePolicyDefinition.None),
            AbortToken
        );

        // then — same scope means same Scoped instance
        observed.Should().NotBeNull();
        observed.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task should_skip_on_exhausted_when_status_already_failed_when_redelivered()
    {
        // given — redelivery where storage is already terminal: the terminal write affects no row.
        var storage = Substitute.For<IDataStorage>();
        var exhausted = 0;
        var executor = _CreateExecutor(
            _AlwaysThrowing(new TimeoutException("boom"), out _),
            storage,
            _OptionsCountingExhausted(() => exhausted++)
        );
        _CaptureWrites(storage, _ => false);

        // when
        await executor.ExecuteAsync(
            _CreateMediumMessage(),
            _EmptyScope,
            _CreateDescriptor(FailurePolicyDefinition.None),
            AbortToken
        );

        // then
        exhausted.Should().Be(0, "OnExhausted must be skipped if storage update returned false");
    }

    // ─── Cancellation ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task should_write_nothing_and_fire_nothing_when_the_host_shuts_down_during_an_attempt()
    {
        using var shutdown = new CancellationTokenSource();
        var storage = Substitute.For<IDataStorage>();
        var invoker = Substitute.For<ISubscribeInvoker>();
        var ruleCalls = 0;
        invoker
            .InvokeAsync(Arg.Any<ConsumerContext>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                await shutdown.CancelAsync();
                call.ArgAt<CancellationToken>(1).ThrowIfCancellationRequested();
                return new ConsumerExecutedResult(null, null, null!, null, null);
            });
        var exhausted = 0;
        var executor = _CreateExecutor(invoker, storage, _OptionsCountingExhausted(() => exhausted++));
        var writes = _CaptureWrites(storage);
        var policy = new FailurePolicyBuilder()
            .Immediate(2)
            .FailWhen(_ =>
            {
                ruleCalls++;
                return true;
            })
            .Build();

        await executor.ExecuteAsync(_CreateMediumMessage(), _EmptyScope, _CreateDescriptor(policy), shutdown.Token);

        await invoker.Received(1).InvokeAsync(Arg.Any<ConsumerContext>(), Arg.Any<CancellationToken>());
        writes.Should().BeEmpty();
        exhausted.Should().Be(0);
        ruleCalls.Should().Be(0, "cancellation with the consume token is never shown to the fail rules");
    }

    // ─── Crash recovery and budget overrun ──────────────────────────────────────────────────────────

    [Fact]
    public async Task should_not_invoke_consumer_when_recovery_finds_reserved_inline_budget_consumed()
    {
        var storage = Substitute.For<IDataStorage>();
        var invoker = Substitute.For<ISubscribeInvoker>();
        var options = new MessagingOptions { RetryPolicy = { DispatchTimeout = TimeSpan.FromSeconds(17) } };
        var executor = _CreateExecutor(invoker, storage, options);
        var writes = _CaptureWrites(storage);
        var message = _CreateMediumMessage();
        // The first dispatch of a 2-immediate policy reserves at most 3 attempts; all 3 were reserved before a crash.
        message.InlineAttempts = 3;

        await executor.ExecuteAsync(message, _EmptyScope, _CreateDescriptor(_Policy(2, 1)), AbortToken);

        await invoker.DidNotReceiveWithAnyArgs().InvokeAsync(null!, AbortToken);
        message.Retries.Should().Be(1);
        message.InlineAttempts.Should().Be(0);
        writes
            .Should()
            .ContainSingle()
            .Which.Should()
            .Match<StateWrite>(write =>
                write.Status == StatusName.Failed
                && write.Delay != null
                && write.LockedUntil == null
                && write.OriginalRetries == 0
                && write.OriginalInlineAttempts == 3
            );
        await storage
            .Received(1)
            .LeaseReceiveAsync(message, options.RetryPolicy.DispatchTimeout, Arg.Any<CancellationToken>());
        await storage
            .DidNotReceiveWithAnyArgs()
            .LeaseReceiveAndReserveAttemptAsync(null!, TimeSpan.Zero, 0, AbortToken);
        await storage.DidNotReceiveWithAnyArgs().ReserveReceiveAttemptAsync(null!, 0, AbortToken);
    }

    [Fact]
    public async Task should_not_invoke_consumer_when_a_delayed_pickup_already_reserved_its_single_attempt()
    {
        // given — a delayed pickup gets one attempt; the crashed dispatch already reserved it.
        var storage = Substitute.For<IDataStorage>();
        var invoker = Substitute.For<ISubscribeInvoker>();
        var executor = _CreateExecutor(invoker, storage, new MessagingOptions());
        var writes = _CaptureWrites(storage);
        var message = _CreateMediumMessage();
        message.Retries = 1;
        message.InlineAttempts = 1;

        await executor.ExecuteAsync(message, _EmptyScope, _CreateDescriptor(_Policy(2, 3)), AbortToken);

        await invoker.DidNotReceiveWithAnyArgs().InvokeAsync(null!, AbortToken);
        message.Retries.Should().Be(2);
        writes.Should().ContainSingle().Which.Delay.Should().NotBeNull();
    }

    [Fact]
    public async Task should_fail_a_row_already_past_its_consumer_budget_without_invoking_the_handler()
    {
        // given — the row used 4 delayed retries, but the consumer's policy now allows only 3.
        var storage = Substitute.For<IDataStorage>();
        var invoker = Substitute.For<ISubscribeInvoker>();
        var exhausted = 0;
        var executor = _CreateExecutor(invoker, storage, _OptionsCountingExhausted(() => exhausted++));
        var writes = _CaptureWrites(storage);
        var message = _CreateMediumMessage();
        message.Retries = 4;

        await executor.ExecuteAsync(message, _EmptyScope, _CreateDescriptor(_Policy(2, 3)), AbortToken);

        await invoker.DidNotReceiveWithAnyArgs().InvokeAsync(null!, AbortToken);
        writes
            .Should()
            .ContainSingle()
            .Which.Should()
            .Match<StateWrite>(write => _IsTerminal(write) && write.OriginalRetries == 4);
        exhausted.Should().Be(1);
    }

    // ─── Storage interaction ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task should_stop_without_invoking_consumer_when_lease_rejects_terminal_row()
    {
        // given — lease returns false (storage proves the row is terminal). Executor must
        // short-circuit without invoking the consumer body and without writing any state.
        var invoker = Substitute.For<ISubscribeInvoker>();
        var storage = Substitute.For<IDataStorage>();
        var executor = _CreateExecutor(invoker, storage, new MessagingOptions());

        // Override the happy-path stub from _CreateExecutor so the fresh-dispatch combined
        // lease+reserve write reports lease contention.
        storage
            .LeaseReceiveAndReserveAttemptAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(false));
        var writes = _CaptureWrites(storage);

        // when
        var result = await executor.ExecuteAsync(_CreateMediumMessage(), _EmptyScope, _CreateDescriptor(), AbortToken);

        // then
        result.Succeeded.Should().BeTrue();
        await invoker.DidNotReceive().InvokeAsync(Arg.Any<ConsumerContext>(), Arg.Any<CancellationToken>());
        writes.Should().BeEmpty();
    }

    [Fact]
    public async Task should_not_invoke_consumer_or_write_state_when_attempt_reservation_is_lost()
    {
        var storage = Substitute.For<IDataStorage>();
        var invoker = Substitute.For<ISubscribeInvoker>();
        var executor = _CreateExecutor(invoker, storage, new MessagingOptions());
        storage
            .ReserveReceiveAttemptAsync(Arg.Any<MediumMessage>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(false));
        var message = _CreateMediumMessage();
        // Storage already acquired this lease. Its absolute expiry is deliberately behind the
        // application clock: core must reserve under the returned identity without reacquiring.
        message.LockedUntil = DateTimeOffset.UnixEpoch.AddMinutes(1);
        message.Owner = "store-owner";

        var result = await executor.ExecuteAsync(message, _EmptyScope, _CreateDescriptor(), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        message.InlineAttempts.Should().Be(0);
        await invoker.DidNotReceiveWithAnyArgs().InvokeAsync(null!, AbortToken);
        await storage
            .DidNotReceiveWithAnyArgs()
            .ChangeReceiveRetryStateAsync(null!, default, default, null, null, 0, 0, AbortToken);
        await storage.DidNotReceiveWithAnyArgs().LeaseReceiveAsync(null!, TimeSpan.Zero, AbortToken);
        await storage
            .DidNotReceiveWithAnyArgs()
            .LeaseReceiveAndReserveAttemptAsync(null!, TimeSpan.Zero, 0, AbortToken);
    }

    [Fact]
    public async Task should_issue_single_combined_storage_write_before_fresh_consume()
    {
        var storage = Substitute.For<IDataStorage>();
        var invoker = Substitute.For<ISubscribeInvoker>();
        invoker
            .InvokeAsync(Arg.Any<ConsumerContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ConsumerExecutedResult(null, null, Guid.NewGuid().ToString(), null, null)));
        var options = new MessagingOptions { RetryPolicy = { DispatchTimeout = TimeSpan.FromSeconds(17) } };
        var executor = _CreateExecutor(invoker, storage, options);
        var message = _CreateMediumMessage();

        var result = await executor.ExecuteAsync(message, _EmptyScope, _CreateDescriptor(), CancellationToken.None);

        // A fresh (never-leased) consume must pay exactly one pre-attempt storage write: the
        // combined lease+reserve statement, never the two-step lease-then-reserve pair.
        result.Succeeded.Should().BeTrue();
        await storage
            .Received(1)
            .LeaseReceiveAndReserveAttemptAsync(
                Arg.Any<MediumMessage>(),
                options.RetryPolicy.DispatchTimeout,
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            );
        await storage.DidNotReceiveWithAnyArgs().LeaseReceiveAsync(null!, TimeSpan.Zero, AbortToken);
        await storage.DidNotReceiveWithAnyArgs().ReserveReceiveAttemptAsync(null!, 0, AbortToken);
    }

    [Fact]
    public async Task should_clear_the_lease_when_an_immediate_retry_succeeds()
    {
        var storage = Substitute.For<IDataStorage>();
        var invoker = Substitute.For<ISubscribeInvoker>();
        var attempts = 0;
        invoker
            .InvokeAsync(Arg.Any<ConsumerContext>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
                ++attempts <= 2
                    ? Task.FromException<ConsumerExecutedResult>(new TimeoutException("boom"))
                    : Task.FromResult(new ConsumerExecutedResult(null, null, Guid.NewGuid().ToString(), null, null))
            );
        var executor = _CreateExecutor(invoker, storage, new MessagingOptions());
        _CaptureWrites(storage);
        var message = _CreateMediumMessage();
        var executionState = new RetryExecutionState();

        var result = await executor.ExecuteRetryAsync(
            message,
            _EmptyScope,
            executionState,
            _CreateDescriptor(_Policy(2, 0)),
            AbortToken
        );

        result.Succeeded.Should().BeTrue();
        attempts.Should().Be(3);
        executionState.LeaseClearedByTransition.Should().BeTrue();
        message.Retries.Should().Be(0, "immediate retries do not count as delayed retries");
    }

    // ─── Helpers ────────────────────────────────────────────────────────────────────────────────────

    private sealed record StateWrite(
        StatusName Status,
        RetryDelay? Delay,
        DateTimeOffset? LockedUntil,
        int OriginalRetries,
        int OriginalInlineAttempts
    );

    private static bool _IsTerminal(StateWrite write) => write.Status == StatusName.Failed && write.Delay is null;

    private static FailurePolicyDefinition _Policy(
        int immediate,
        int delayed,
        TimeSpan? initialDelay = null,
        TimeSpan? maxDelay = null
    )
    {
        var builder = new FailurePolicyBuilder().Immediate(immediate);
        if (delayed > 0)
        {
            builder.Delayed(delayed, initialDelay ?? TimeSpan.FromSeconds(1), maxDelay ?? TimeSpan.FromMinutes(1));
        }

        return builder.Build();
    }

    /// <summary>The range a jittered delay for delayed retry <paramref name="n"/> may take, derived from the policy formula.</summary>
    private static (TimeSpan Lower, TimeSpan Upper) _JitterBand(TimeSpan initial, TimeSpan cap, int n)
    {
        var baseSeconds = Math.Min(initial.TotalSeconds * Math.Pow(2, n - 1), cap.TotalSeconds);
        var lower = TimeSpan.FromSeconds(baseSeconds * (1 - FailurePolicyDefinition.JitterFraction));
        var upper = TimeSpan.FromSeconds(
            Math.Min(baseSeconds * (1 + FailurePolicyDefinition.JitterFraction), cap.TotalSeconds)
        );
        return (lower - TimeSpan.FromMilliseconds(1), upper + TimeSpan.FromMilliseconds(1));
    }

    private static ISubscribeInvoker _AlwaysThrowing(Exception exception, out Func<int> invocations)
    {
        var count = 0;
        var invoker = Substitute.For<ISubscribeInvoker>();
        invoker
            .InvokeAsync(Arg.Any<ConsumerContext>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Interlocked.Increment(ref count);
                return Task.FromException<ConsumerExecutedResult>(exception);
            });
        invocations = () => Volatile.Read(ref count);
        return invoker;
    }

    private static MessagingOptions _OptionsCountingExhausted(Action onExhausted)
    {
        return _OptionsCountingExhausted(_ => onExhausted());
    }

    private static MessagingOptions _OptionsCountingExhausted(Action<FailedInfo> onExhausted)
    {
        return new MessagingOptions
        {
            RetryPolicy =
            {
                OnExhausted = (info, _) =>
                {
                    onExhausted(info);
                    return Task.CompletedTask;
                },
            },
        };
    }

    /// <summary>
    /// Records every retry-state write. Call after <see cref="_CreateExecutor"/>, whose happy-path stub this replaces.
    /// </summary>
    private static List<StateWrite> _CaptureWrites(IDataStorage storage, Func<StateWrite, bool>? affected = null)
    {
        var writes = new List<StateWrite>();
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
            .Returns(call =>
            {
                var write = new StateWrite(
                    call.ArgAt<StatusName>(1),
                    call.ArgAt<RetryDelay?>(3),
                    call.ArgAt<DateTimeOffset?>(4),
                    call.ArgAt<int>(5),
                    call.ArgAt<int>(6)
                );
                lock (writes)
                {
                    writes.Add(write);
                }

                return ValueTask.FromResult(affected?.Invoke(write) ?? true);
            });
        return writes;
    }

    private static async Task<int> _DispatchUntilTerminalAsync(
        SubscribeExecutor executor,
        MediumMessage message,
        ConsumerExecutorDescriptor descriptor,
        List<StateWrite> writes,
        int maxDispatches
    )
    {
        var dispatches = 0;
        while (!writes.Exists(_IsTerminal) && dispatches < maxDispatches)
        {
            dispatches++;
            await executor.ExecuteRetryAsync(message, _EmptyScope, new RetryExecutionState(), descriptor, AbortToken);
        }

        return dispatches;
    }

    private sealed class ScopedMarker;
}
