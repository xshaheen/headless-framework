// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Messaging.Persistence;
using Headless.Messaging.Retry;
using Headless.Reliability;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tests.RequestReply;

namespace Tests;

/// <summary>
/// A stored message can wait in the inbox, behind retries, or for a crashed attempt's lease long after its delivery
/// deadline. Every attempt start checks it on the consumer host's clock and ends an expired message without running
/// the consumer, firing the exhausted callback, or counting against the circuit; an operator replay drops the deadline.
/// </summary>
public sealed class DeliverByAttemptTests : TestBase
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_end_terminally_without_invoking_consumer_when_delivery_deadline_passed_before_attempt(
        bool persistedPickup
    )
    {
        // given — admitted before its deadline, dispatched (or picked up again by the retry processor) after it
        await using var host = ResponderExecutorHost.Create();
        var message = _PlainMessage(host, TimeSpan.FromSeconds(5));
        if (persistedPickup)
        {
            message.LockedUntil = host.Clock.GetUtcNow().AddMinutes(5);
        }

        host.Clock.Advance(TimeSpan.FromSeconds(6));

        // when
        var result = await host.ExecuteAsync(message, AbortToken);

        // then — terminal with the reason, and silent: no consumer, no exhausted callback
        result.Succeeded.Should().BeFalse();
        result.Exception.Should().BeOfType<MessageDeliverByExceededException>();
        host.Invoker.ReceivedCalls().Should().BeEmpty();
        host.StateWrites().Should().ContainSingle().Which.Should().Be((StatusName.Failed, (RetryDelay?)null));
        host.ExhaustedCalls.Should().Be(0);
        message.ExceptionInfo.Should().Contain("deliver_by_exceeded");
    }

    [Fact]
    public async Task should_end_crash_recovered_message_as_expired_without_the_exhausted_callback()
    {
        // given — the process died during the final inline attempt, and the row is picked up after the deadline
        await using var host = ResponderExecutorHost.Create();
        var message = _PlainMessage(host, TimeSpan.FromSeconds(5));
        message.InlineAttempts = 2;
        host.Clock.Advance(TimeSpan.FromSeconds(6));

        // when
        var result = await host.ExecuteAsync(
            message,
            AbortToken,
            ResponderExecutorHost.ResponderDescriptor(failurePolicy: new FailurePolicyBuilder().Immediate(1).Build())
        );

        // then — the would-be exhaustion becomes an expiry
        result.Exception.Should().BeOfType<MessageDeliverByExceededException>();
        host.Invoker.ReceivedCalls().Should().BeEmpty();
        host.StateWrites().Should().ContainSingle().Which.Should().Be((StatusName.Failed, (RetryDelay?)null));
        host.ExhaustedCalls.Should().Be(0);
    }

    [Fact]
    public async Task should_release_the_probe_without_reporting_a_failure_when_delivery_deadline_passed()
    {
        // given — the same executor wired to a circuit breaker
        await using var host = ResponderExecutorHost.Create();
        var circuitBreaker = Substitute.For<ICircuitBreakerStateManager>();
        var executor = new SubscribeExecutor(
            host.Provider,
            host.Storage,
            host.Invoker,
            host.Clock,
            host.Provider.GetRequiredService<ILogger<SubscribeExecutor>>(),
            host.Provider.GetRequiredService<IOptions<MessagingOptions>>(),
            circuitBreaker
        );
        var message = _PlainMessage(host, TimeSpan.FromSeconds(5));
        host.Clock.Advance(TimeSpan.FromSeconds(6));

        // when
        await executor.ExecuteAsync(message, host.Provider, ResponderExecutorHost.ResponderDescriptor(), AbortToken);

        // then — an expiry is no consumer failure, so it neither counts toward nor holds the circuit
        await circuitBreaker
            .DidNotReceive()
            .ReportFailureAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<Exception>(), Arg.Any<CancellationToken>());
        circuitBreaker.Received(1).ReleaseHalfOpenProbe(Arg.Any<string>(), Arg.Any<long>());
    }

    [Fact]
    public async Task should_run_the_consumer_when_delivery_deadline_is_still_ahead()
    {
        // given
        await using var host = ResponderExecutorHost.Create();
        var message = _PlainMessage(host, TimeSpan.FromSeconds(30));

        // when
        var result = await host.ExecuteAsync(message, AbortToken);

        // then
        result.Succeeded.Should().BeTrue();
        host.Invoker.ReceivedCalls().Should().ContainSingle();
        host.StateWrites().Should().ContainSingle().Which.Status.Should().Be(StatusName.Succeeded);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_strip_delivery_deadline_and_run_the_consumer_when_an_operator_replays(bool persistedPickup)
    {
        // given — an operator forced a child generation of a message whose deadline passed long ago
        await using var host = ResponderExecutorHost.Create();
        var message = _PlainMessage(host, TimeSpan.FromSeconds(5), generation: 1);
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

        // then — the replay runs for the operator, past the publisher's deadline
        result.Succeeded.Should().BeTrue();
        host.Invoker.ReceivedCalls().Should().ContainSingle();
        message.Origin.Headers.Should().NotContainKey(Headers.DeliverBy);
        host.StateWrites().Should().ContainSingle().Which.Status.Should().Be(StatusName.Succeeded);
    }

    [Fact]
    public async Task should_keep_expiring_the_delivered_generation_of_a_message()
    {
        // given — the same expired envelope on generation 0, the delivery itself
        await using var host = ResponderExecutorHost.Create();
        var message = _PlainMessage(host, TimeSpan.FromSeconds(5), generation: 0);
        host.Clock.Advance(TimeSpan.FromSeconds(6));

        // when
        var result = await host.ExecuteAsync(message, AbortToken);

        // then — still bounded by its deadline, and the envelope is untouched
        result.Succeeded.Should().BeFalse();
        host.Invoker.ReceivedCalls().Should().BeEmpty();
        message.Origin.Headers.Should().ContainKey(Headers.DeliverBy);
    }

    private static MediumMessage _PlainMessage(
        ResponderExecutorHost host,
        TimeSpan timeUntilDeadline,
        long? generation = null
    )
    {
        var message = host.Request(TimeSpan.Zero, asRequest: false, generation: generation);
        message.Origin.Headers[Headers.DeliverBy] = host
            .Clock.GetUtcNow()
            .Add(timeUntilDeadline)
            .ToString("O", CultureInfo.InvariantCulture);
        return message;
    }
}
