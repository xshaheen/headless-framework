// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Messaging.Retry;
using Headless.Reliability;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tests.Helpers;

namespace Tests.RequestReply;

/// <summary>
/// A request carries the instant its caller stops waiting. The responder honors it on its own clock: it starts no
/// attempt after it, and it never schedules a retry that would start after it or that waits for the persisted retry
/// processor, because the caller is gone by then.
/// </summary>
public sealed class RequestDeadlineTests : TestBase
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_end_an_expired_request_terminally_without_invoking_the_handler(bool persistedPickup)
    {
        // given — admitted before its deadline, dispatched (or picked up again by the retry processor) after it
        await using var host = ResponderExecutorHost.Create();
        var message = host.Request(TimeSpan.FromSeconds(5));
        if (persistedPickup)
        {
            message.LockedUntil = host.Clock.GetUtcNow().AddMinutes(5);
        }

        host.Clock.Advance(TimeSpan.FromSeconds(6));

        // when
        var result = await host.ExecuteAsync(message, AbortToken);

        // then — terminal, silent: no handler, no exhausted callback, no reply
        result.Succeeded.Should().BeFalse();
        host.Invoker.ReceivedCalls().Should().BeEmpty();
        host.StateWrites().Should().ContainSingle().Which.Should().Be((StatusName.Failed, (RetryDelay?)null));
        host.ExhaustedCalls.Should().Be(0);
        host.Replies.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task should_end_a_crash_recovered_request_as_expired_without_the_exhausted_callback_or_a_fault()
    {
        // given — the process died during the request's final inline attempt, and the row is picked up after the
        // deadline: the reserved attempt count already covers the whole inline budget
        await using var host = ResponderExecutorHost.Create();
        var message = host.Request(TimeSpan.FromSeconds(5));
        message.InlineAttempts = 2;
        host.Clock.Advance(TimeSpan.FromSeconds(6));

        // when
        var result = await host.ExecuteAsync(message, AbortToken, _Responder(new FailurePolicyBuilder().Immediate(1)));

        // then — ended as expired: no handler, no exhausted callback, no fault to a caller that is gone
        result.Succeeded.Should().BeFalse();
        host.Invoker.ReceivedCalls().Should().BeEmpty();
        host.StateWrites().Should().ContainSingle().Which.Should().Be((StatusName.Failed, (RetryDelay?)null));
        host.ExhaustedCalls.Should().Be(0);
        host.Replies.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task should_run_the_consumer_of_a_bus_message_that_carries_an_expired_request_deadline()
    {
        // given — request headers on a Bus message, where no request can live
        await using var host = ResponderExecutorHost.Create();
        var message = host.Request(TimeSpan.FromSeconds(5));
        message.Lane = MessageLane.Bus;
        host.Clock.Advance(TimeSpan.FromSeconds(6));
        var busConsumer = new ConsumerExecutorDescriptor
        {
            MethodName = "ConsumeAsync",
            Lane = MessageLane.Bus,
            ConsumerType = typeof(QuoteResponder),
            MessageType = typeof(PriceQuoteRequest),
            MessageName = ResponderExecutorHost.MessageName,
            SubscriptionName = "tests",
            ConsumerIdentity = QuoteResponder.Identity,
            MessageContractVersion = "1",
        };

        // when
        var result = await host.ExecuteAsync(message, AbortToken, busConsumer);

        // then — consumed like any Bus message: no expiry, no reply
        result.Succeeded.Should().BeTrue();
        host.Invoker.ReceivedCalls().Should().ContainSingle();
        host.Replies.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task should_treat_a_request_as_expired_when_the_responder_clock_runs_ahead_of_the_caller()
    {
        // given — the caller stamped a 5 s deadline, but the responder's clock is 10 s ahead of the caller's
        await using var host = ResponderExecutorHost.Create();
        var message = host.Request(TimeSpan.FromSeconds(5));
        host.Clock.Advance(TimeSpan.FromSeconds(10));

        // when
        await host.ExecuteAsync(message, AbortToken);

        // then — the deadline is read on the responder's clock, so skew shortens the window
        host.Invoker.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task should_end_terminally_when_the_deadline_passes_during_an_attempt()
    {
        // given — three immediate retries, but the attempt fails only after the caller's 5 s deadline
        await using var host = ResponderExecutorHost.Create();
        host.OnInvoke(() =>
        {
            host.Clock.Advance(TimeSpan.FromSeconds(6));
            return Task.FromException<ConsumerExecutedResult>(new TimeoutException("transient"));
        });

        // when
        var result = await host.ExecuteAsync(
            host.Request(TimeSpan.FromSeconds(5)),
            AbortToken,
            _Responder(new FailurePolicyBuilder().Immediate(3))
        );

        // then — one attempt, then a terminal write and a fault instead of an immediate retry
        result.Succeeded.Should().BeFalse();
        host.Invoker.ReceivedCalls().Should().ContainSingle();
        host.StateWrites().Should().ContainSingle().Which.Should().Be((StatusName.Failed, (RetryDelay?)null));
        host.FaultCodes().Should().Equal(RequestFaultCodes.HandlerFailed);
    }

    [Fact]
    public async Task should_end_terminally_instead_of_scheduling_a_delayed_retry_when_the_immediate_retries_run_out()
    {
        // given — one immediate retry, then the failure policy would hand the row to the persisted retry processor
        await using var host = ResponderExecutorHost.Create();
        host.OnInvoke(() => Task.FromException<ConsumerExecutedResult>(new TimeoutException("transient")));
        var message = host.Request(TimeSpan.FromSeconds(30));

        // when
        var result = await host.ExecuteAsync(
            message,
            AbortToken,
            _Responder(
                new FailurePolicyBuilder().Immediate(1).Delayed(15, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(15))
            )
        );

        // then — both immediate attempts ran, and the last write is terminal with no delayed retry scheduled
        result.Succeeded.Should().BeFalse();
        host.Invoker.ReceivedCalls().Should().HaveCount(2);
        host.StateWrites()[^1].Should().Be((StatusName.Failed, (RetryDelay?)null));
        message.Retries.Should().Be(0);
        host.FaultCodes().Should().Equal(RequestFaultCodes.HandlerFailed);
    }

    [Fact]
    public async Task should_retry_immediately_while_the_deadline_leaves_time()
    {
        // given — the first attempt fails transiently, the second succeeds
        await using var host = ResponderExecutorHost.Create();
        var attempts = 0;
        host.OnInvoke(() =>
            ++attempts == 1
                ? Task.FromException<ConsumerExecutedResult>(new TimeoutException("transient"))
                : Task.FromResult(ResponderExecutorHost.Replied(new PriceQuote(7)))
        );

        // when
        var result = await host.ExecuteAsync(
            host.Request(TimeSpan.FromSeconds(30)),
            AbortToken,
            _Responder(new FailurePolicyBuilder().Immediate(2))
        );

        // then — one reply, from the attempt that succeeded
        result.Succeeded.Should().BeTrue();
        attempts.Should().Be(2);
        host.Replies.Sent.Should().ContainSingle().Which.Reply.Headers[Headers.ReplyStatus].Should().Be("ok");
    }

    [Fact]
    public async Task should_stop_after_one_attempt_when_the_failure_policy_ends_the_failure_at_once()
    {
        // given — NotSupportedException is in the built-in permanent set
        await using var host = ResponderExecutorHost.Create();
        host.OnInvoke(() => Task.FromException<ConsumerExecutedResult>(new NotSupportedException("bad sku")));

        // when
        var result = await host.ExecuteAsync(host.Request(TimeSpan.FromSeconds(30)), AbortToken);

        // then — the caller gets the fault at once, and the terminal failure fires the exhausted callback
        result.Succeeded.Should().BeFalse();
        host.Invoker.ReceivedCalls().Should().ContainSingle();
        host.StateWrites().Should().ContainSingle().Which.Should().Be((StatusName.Failed, (RetryDelay?)null));
        host.FaultCodes().Should().Equal(RequestFaultCodes.HandlerFailed);
        host.ExhaustedCalls.Should().Be(1);
    }

    [Fact]
    public async Task should_keep_the_failure_policy_for_a_plain_queue_message()
    {
        // given — the same failure on a message that carries no request headers
        await using var host = ResponderExecutorHost.Create();
        host.OnInvoke(() => Task.FromException<ConsumerExecutedResult>(new TimeoutException("transient")));
        var message = host.Request(TimeSpan.Zero, asRequest: false);

        // when
        await host.ExecuteAsync(
            message,
            AbortToken,
            _Responder(new FailurePolicyBuilder().Delayed(3, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(15)))
        );

        // then — handed to the persisted retry processor, exactly as for any consumer
        var (status, nextRetry) = host.StateWrites().Should().ContainSingle().Subject;
        status.Should().Be(StatusName.Failed);
        nextRetry.Should().NotBeNull();
        message.Retries.Should().Be(1);
        host.Replies.Sent.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_run_a_replayed_request_as_a_plain_queue_message_without_replying(bool persistedPickup)
    {
        // given — an operator forced a child generation from an expired request; the child carries the parent's
        // envelope verbatim, reply address and deadline included
        var log = new List<(LogLevel Level, EventId EventId)>();
        await using var host = ResponderExecutorHost.Create(configureServices: services =>
            services.AddLogging(logging => logging.AddProvider(new CapturingLoggerProvider(log)))
        );
        host.OnInvoke(() => Task.FromResult(ResponderExecutorHost.Replied(new PriceQuote(7))));
        var message = host.Request(TimeSpan.FromSeconds(5), generation: 1);
        var requestId = message.Origin.Headers[Headers.RequestId];
        host.Clock.Advance(TimeSpan.FromMinutes(10));

        // when
        var result = persistedPickup
            ? await host.Executor.ExecuteRetryAsync(
                message,
                host.Provider,
                new RetryExecutionState(),
                ResponderExecutorHost.ResponderDescriptor(),
                AbortToken
            )
            : await host.ExecuteAsync(message, AbortToken);

        // then — the responder ran for the operator: success written, nobody answered, the envelope is no request
        result.Succeeded.Should().BeTrue();
        host.Invoker.ReceivedCalls().Should().ContainSingle();
        host.StateWrites().Should().ContainSingle().Which.Status.Should().Be(StatusName.Succeeded);
        host.Replies.Sent.Should().BeEmpty();
        message.Origin.Headers.Should().NotContainKey(Headers.ReplyTo).And.NotContainKey(Headers.RequestDeadline);
        message.Origin.Headers[Headers.RequestId].Should().Be(requestId);
        log.Count(entry => entry.EventId.Id == 4116).Should().Be(1);
    }

    [Fact]
    public async Task should_give_a_replayed_request_the_full_failure_policy_without_a_fault()
    {
        // given — the replayed consumer fails transiently under a policy that hands retries to the persisted processor
        await using var host = ResponderExecutorHost.Create();
        host.OnInvoke(() => Task.FromException<ConsumerExecutedResult>(new TimeoutException("transient")));
        var message = host.Request(TimeSpan.FromSeconds(5), generation: 1);
        host.Clock.Advance(TimeSpan.FromSeconds(6));

        // when
        await host.ExecuteAsync(
            message,
            AbortToken,
            _Responder(new FailurePolicyBuilder().Delayed(3, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(15)))
        );

        // then — a delayed retry is scheduled, as for any Queue message, and no caller is told anything
        var (status, nextRetry) = host.StateWrites().Should().ContainSingle().Subject;
        status.Should().Be(StatusName.Failed);
        nextRetry.Should().NotBeNull();
        message.Retries.Should().Be(1);
        host.Replies.Sent.Should().BeEmpty();
        host.ExhaustedCalls.Should().Be(0);
    }

    [Fact]
    public async Task should_end_a_replayed_request_terminally_without_a_fault_when_its_consumer_is_gone()
    {
        // given — the child generation names a consumer this host no longer registers
        await using var host = ResponderExecutorHost.Create();
        var message = host.Request(TimeSpan.FromSeconds(30), generation: 1);

        // when
        var result = await host.Executor.ExecuteAsync(message, host.Provider, descriptor: null, AbortToken);

        // then — terminal, with the exhausted callback a lost consumer always gets, and no fault to anyone
        result.Succeeded.Should().BeFalse();
        host.Invoker.ReceivedCalls().Should().BeEmpty();
        host.StateWrites().Should().ContainSingle().Which.Should().Be((StatusName.Failed, (RetryDelay?)null));
        host.ExhaustedCalls.Should().Be(1);
        host.Replies.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task should_keep_expiring_the_delivered_generation_of_a_request()
    {
        // given — the same expired envelope on generation 0, the delivery itself
        await using var host = ResponderExecutorHost.Create();
        var message = host.Request(TimeSpan.FromSeconds(5), generation: 0);
        host.Clock.Advance(TimeSpan.FromSeconds(6));

        // when
        var result = await host.ExecuteAsync(message, AbortToken);

        // then — still a request: it expires, and its envelope is untouched
        result.Succeeded.Should().BeFalse();
        host.Invoker.ReceivedCalls().Should().BeEmpty();
        message.Origin.Headers.Should().ContainKey(Headers.ReplyTo).And.ContainKey(Headers.RequestDeadline);
    }

    private static ConsumerExecutorDescriptor _Responder(FailurePolicyBuilder policy) =>
        ResponderExecutorHost.ResponderDescriptor(failurePolicy: policy.Build());
}
