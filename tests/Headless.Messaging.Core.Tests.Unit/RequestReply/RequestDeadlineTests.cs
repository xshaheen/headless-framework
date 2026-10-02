// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Testing.Tests;

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
        await using var host = ResponderExecutorHost.Create(options =>
            options.RetryPolicy.RetryStrategy = TestRetryStrategies.ZeroDelay(1)
        );
        var message = host.Request(TimeSpan.FromSeconds(5));
        message.InlineAttempts = 2;
        host.Clock.Advance(TimeSpan.FromSeconds(6));

        // when
        var result = await host.ExecuteAsync(message, AbortToken);

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
    public async Task should_end_terminally_when_the_next_inline_delay_would_start_after_the_deadline()
    {
        // given — three inline retries 10 s apart, but the caller waits only 5 s
        await using var host = ResponderExecutorHost.Create(options =>
            options.RetryPolicy.RetryStrategy = TestRetryStrategies.FixedDelay(3, TimeSpan.FromSeconds(10))
        );
        host.OnInvoke(() => Task.FromException<ConsumerExecutedResult>(new TimeoutException("transient")));

        // when
        var result = await host.ExecuteAsync(host.Request(TimeSpan.FromSeconds(5)), AbortToken);

        // then — one attempt, then a terminal write and a fault instead of an inline retry
        result.Succeeded.Should().BeFalse();
        host.Invoker.ReceivedCalls().Should().ContainSingle();
        host.StateWrites().Should().ContainSingle().Which.Should().Be((StatusName.Failed, (RetryDelay?)null));
        host.FaultCodes().Should().Equal(RequestFaultCodes.HandlerFailed);
    }

    [Fact]
    public async Task should_end_terminally_instead_of_scheduling_a_persisted_retry_when_the_inline_budget_runs_out()
    {
        // given — one inline retry, then the host policy would hand the row to the persisted retry processor
        await using var host = ResponderExecutorHost.Create(options =>
        {
            options.RetryPolicy.RetryStrategy = TestRetryStrategies.ZeroDelay(1);
            options.RetryPolicy.MaxPersistedRetries = 15;
        });
        host.OnInvoke(() => Task.FromException<ConsumerExecutedResult>(new TimeoutException("transient")));
        var message = host.Request(TimeSpan.FromSeconds(30));

        // when
        var result = await host.ExecuteAsync(message, AbortToken);

        // then — both inline attempts ran, and the last write is terminal with no persisted retry scheduled
        result.Succeeded.Should().BeFalse();
        host.Invoker.ReceivedCalls().Should().HaveCount(2);
        host.StateWrites()[^1].Should().Be((StatusName.Failed, (RetryDelay?)null));
        message.Retries.Should().Be(0);
        host.FaultCodes().Should().Equal(RequestFaultCodes.HandlerFailed);
    }

    [Fact]
    public async Task should_retry_inline_while_the_deadline_leaves_time()
    {
        // given — the first attempt fails transiently, the second succeeds
        await using var host = ResponderExecutorHost.Create(options =>
            options.RetryPolicy.RetryStrategy = TestRetryStrategies.ZeroDelay(2)
        );
        var attempts = 0;
        host.OnInvoke(() =>
            ++attempts == 1
                ? Task.FromException<ConsumerExecutedResult>(new TimeoutException("transient"))
                : Task.FromResult(ResponderExecutorHost.Replied(new PriceQuote(7)))
        );

        // when
        var result = await host.ExecuteAsync(host.Request(TimeSpan.FromSeconds(30)), AbortToken);

        // then — one reply, from the attempt that succeeded
        result.Succeeded.Should().BeTrue();
        attempts.Should().Be(2);
        host.Replies.Sent.Should().ContainSingle().Which.Reply.Headers[Headers.ReplyStatus].Should().Be("ok");
    }

    [Fact]
    public async Task should_stop_after_one_attempt_when_the_host_classifies_the_failure_as_permanent()
    {
        // given — the default classifier treats NotSupportedException as permanent
        await using var host = ResponderExecutorHost.Create();
        host.OnInvoke(() => Task.FromException<ConsumerExecutedResult>(new NotSupportedException("bad sku")));

        // when
        var result = await host.ExecuteAsync(host.Request(TimeSpan.FromSeconds(30)), AbortToken);

        // then — the caller gets the fault at once, while the attempt-start expiry above sends nothing
        result.Succeeded.Should().BeFalse();
        host.Invoker.ReceivedCalls().Should().ContainSingle();
        host.StateWrites().Should().ContainSingle().Which.Should().Be((StatusName.Failed, (RetryDelay?)null));
        host.FaultCodes().Should().Equal(RequestFaultCodes.HandlerFailed);
        host.ExhaustedCalls.Should().Be(0);
    }

    [Fact]
    public async Task should_keep_the_host_retry_policy_for_a_plain_queue_message()
    {
        // given — the same failure on a message that carries no request headers
        await using var host = ResponderExecutorHost.Create(options =>
        {
            options.RetryPolicy.RetryStrategy = TestRetryStrategies.ZeroDelay(0);
            options.RetryPolicy.MaxPersistedRetries = 3;
        });
        host.OnInvoke(() => Task.FromException<ConsumerExecutedResult>(new TimeoutException("transient")));
        var message = host.Request(TimeSpan.Zero, asRequest: false);

        // when
        await host.ExecuteAsync(message, AbortToken);

        // then — handed to the persisted retry processor, exactly as before
        var (status, nextRetry) = host.StateWrites().Should().ContainSingle().Subject;
        status.Should().Be(StatusName.Failed);
        nextRetry.Should().NotBeNull();
        message.Retries.Should().Be(1);
        host.Replies.Sent.Should().BeEmpty();
    }
}
