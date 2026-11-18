// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tests.Simulation;

namespace Tests.Consume;

/// <summary>A queue message for the rate-limited consumer; its <see cref="Id"/> names it in the probe's records.</summary>
public sealed record ThrottledPipelineOrder(string Id)
{
    public const string Name = "tests.pipeline.throttled-order";
}

/// <summary>Hands every <see cref="ThrottledPipelineOrder"/> to the test's <see cref="PipelineProbe"/>.</summary>
[QueueConsumer(Identity)]
public sealed class ThrottledPipelineOrderConsumer(PipelineProbe probe, TimeProvider clock)
    : IConsume<ThrottledPipelineOrder>
{
    public const string Identity = "tests.pipeline.throttled-orders";

    public ValueTask ConsumeAsync(ConsumeContext<ThrottledPipelineOrder> context, CancellationToken cancellationToken)
    {
        return probe.HandleAsync(context.Message.Id, clock.GetUtcNow(), cancellationToken);
    }
}

/// <summary>
/// A rate-limited consumer beside another consumer on the real consume pipeline, against the simulated broker and
/// store: a delivery waiting for a permit must not hold the other consumer's persisted retry.
/// </summary>
public sealed class ConsumerRateLimitRetryTests : TestBase
{
    private const string _Retried = "retried-order";
    private const string _FirstThrottled = "throttled-1";
    private const string _WaitingThrottled = "throttled-2";

    // Longer than the 30 s dispatch grace, so the waiting delivery's row is due before the other consumer's retry and
    // sits ahead of it in the same pickup.
    private static readonly TimeSpan _RetryDelay = TimeSpan.FromSeconds(60);

    // A delayed retry falls due within its delay plus 20% jitter, and retry pickup sees it on its next poll.
    private static readonly TimeSpan _PastRetryDelay = (_RetryDelay * 1.2) + PipelineHosts.PollInterval;

    [Fact]
    public async Task should_run_another_consumers_due_retry_while_a_throttled_delivery_waits_for_its_permit()
    {
        // given: one permit and no second one for an hour, so the second throttled delivery waits for the whole test
        var probe = new PipelineProbe
        {
            Behavior = static (id, attempt, _) =>
                string.Equals(id, _Retried, StringComparison.Ordinal) && attempt == 1
                    ? throw new InvalidOperationException("boom")
                    : ValueTask.CompletedTask,
        };
        await using var host = await PipelineHosts.StartAsync(
            probe,
            AbortToken,
            static setup =>
            {
                setup.AddConsumer<ThrottledPipelineOrderConsumer>();
                setup.DefaultFailurePolicy(policy => policy.Delayed(1, _RetryDelay, _RetryDelay));
                setup.Tune(
                    ThrottledPipelineOrderConsumer.Identity,
                    consumer =>
                        consumer.Concurrency(4).RateLimit(ConsumerRateLimit.TokenBucket(1, 1, TimeSpan.FromHours(1)))
                );
            },
            configureServices: static services =>
                services.ConfigureMessaging(messaging =>
                    messaging.Message<ThrottledPipelineOrder>(ThrottledPipelineOrder.Name)
                )
        );

        await _EnqueueThrottledAsync(host, _FirstThrottled);
        await host.WithinBoundAsync(probe.WhenCompletedAsync(_FirstThrottled, 1), AbortToken);
        await _EnqueueThrottledAsync(host, _WaitingThrottled);

        // and: the other consumer fails once, which schedules its retry
        await host.EnqueueAsync(_Retried, AbortToken);
        await host.WaitForRowAsync(
            row => row.StatusName == StatusName.Failed && row.NextRetryAt > host.Clock.GetUtcNow(),
            AbortToken
        );

        // when: one pickup claims the waiting delivery's row, past its dispatch grace, and the due retry behind it. The
        // clock jumps past the grace at once, before the delivery could put the row's pickup off, so the claim lands.
        await host.RunRetryPickupAsync(_PastRetryDelay, AbortToken);

        // then: the retry ran, and the pickup finished without the throttled row
        await host.WithinBoundAsync(probe.WhenCompletedAsync(_Retried, 1), AbortToken);
        probe.Attempts(_Retried).Should().Be(2);
        probe.Attempts(_WaitingThrottled).Should().Be(0);

        // and: the throttled row is put back unleased for its own delivery, due again later
        var throttledRows = await _ThrottledRowsAsync(host);
        var waiting = throttledRows.Should().ContainSingle(row => row.StatusName == StatusName.Scheduled).Subject;
        waiting.NextRetryAt.Should().BeAfter(host.Clock.GetUtcNow());
        waiting.LockedUntil.Should().BeNull();
    }

    [Fact]
    public async Task should_keep_retry_pickup_off_a_row_whose_delivery_waits_for_its_permit()
    {
        // given: one permit and no second one for an hour, so the second delivery waits for its permit all test long.
        // A pickup that leased its row could not let it go before that permit might arrive, and the delivery's own
        // lease take would then be refused, spending the permit without running the handler.
        var probe = new PipelineProbe();
        await using var host = await PipelineHosts.StartAsync(
            probe,
            AbortToken,
            static setup =>
            {
                setup.AddConsumer<ThrottledPipelineOrderConsumer>();
                setup.Tune(
                    ThrottledPipelineOrderConsumer.Identity,
                    consumer =>
                        consumer.Concurrency(4).RateLimit(ConsumerRateLimit.TokenBucket(1, 1, TimeSpan.FromHours(1)))
                );
            },
            configureServices: _ConfigureServices
        );
        var policy = host.GetRequiredService<IOptions<MessagingOptions>>().Value.RetryPolicy;
        var grace = policy.InitialDispatchGrace;
        var quarterGrace = grace / 4;
        var renewal = policy.ResolveLeaseRenewalInterval();
        var keeperTick = quarterGrace < renewal ? quarterGrace : renewal;

        await _EnqueueThrottledAsync(host, _FirstThrottled);
        await host.WithinBoundAsync(probe.WhenCompletedAsync(_FirstThrottled, 1), AbortToken);
        var beforeWait = host.Clock.WaitMark;
        await _EnqueueThrottledAsync(host, _WaitingThrottled);
        await _WaitForThrottledRowAsync(host, static row => row.StatusName == StatusName.Scheduled);

        // and: the delivery waits for its permit, and the keeper of its row has armed its first pass. The clock does
        // not move before then, so every pass falls where the test expects it.
        await _WaitForArmedAsync(host, beforeWait, keeperTick);
        var storedAt = host.Clock.GetUtcNow();

        // when: time runs pass by pass past its dispatch grace and several graces further, with a pickup on each pass
        while (host.Clock.GetUtcNow() - storedAt < grace * 4)
        {
            var mark = host.Clock.WaitMark;
            host.Advance(keeperTick);

            // A pass arms the next one only once its hold is written, so the pickup this advance fired never meets a
            // hold in flight.
            await _WaitForArmedAsync(host, mark, keeperTick);
            await host.WhenRetryPickupParkedAsync(AbortToken);

            if (host.Clock.GetUtcNow() - storedAt >= grace)
            {
                host.Storage.CallsFinished(StorageOperation.DeferReceivedDispatch)
                    .Should()
                    .BePositive("the row must be held before its dispatch grace runs out");
            }
        }

        // then: no pickup claimed the row, so none had to hand it back, and no host can claim it now
        host.Storage.CallsFinished(StorageOperation.DeferReceivedRetry).Should().Be(0);
        var claimable = await host.Storage.Inner.GetReceivedMessagesOfNeedRetryAsync(
            MessageLane.Queue,
            consumerIdentities: null,
            AbortToken
        );
        claimable.Should().BeEmpty("the pickup of a row whose delivery waits for its permit keeps being put off");
        probe.Attempts(_WaitingThrottled).Should().Be(0);

        var waiting = (await _ThrottledRowsAsync(host))
            .Should()
            .ContainSingle(static row => row.StatusName == StatusName.Scheduled)
            .Subject;
        waiting.LockedUntil.Should().BeNull();
    }

    [Fact]
    public async Task should_put_a_row_back_when_its_immediate_retry_finds_no_permit_on_the_pickup_task()
    {
        // given: one permit an hour, and a throttled delivery that fails. A row runs immediate retries only on its first
        // dispatch, so a pickup runs them only for a row whose delivery never ran: here, one that was still waiting
        // for its permit when its host stopped.
        var probe = new PipelineProbe
        {
            Behavior = static (id, attempt, _) =>
                string.Equals(id, _WaitingThrottled, StringComparison.Ordinal)
                || (string.Equals(id, _Retried, StringComparison.Ordinal) && attempt == 1)
                    ? throw new InvalidOperationException("boom")
                    : ValueTask.CompletedTask,
        };
        var stopped = await PipelineHosts.StartAsync(
            probe,
            AbortToken,
            _ConfigureThrottled,
            configureServices: _ConfigureServices
        );
        await using (stopped)
        {
            await _EnqueueThrottledAsync(stopped, _FirstThrottled);
            await stopped.WithinBoundAsync(probe.WhenCompletedAsync(_FirstThrottled, 1), AbortToken);
            await _EnqueueThrottledAsync(stopped, _WaitingThrottled);
            await _WaitForThrottledRowAsync(stopped, static row => row.StatusName == StatusName.Scheduled);

            // and: the other consumer fails once, which schedules its retry
            await stopped.EnqueueAsync(_Retried, AbortToken);
            await stopped.WaitForRowAsync(
                row => row.StatusName == StatusName.Failed && row.NextRetryAt > stopped.Clock.GetUtcNow(),
                AbortToken
            );
            await stopped.ShutDownAsync(AbortToken);
        }

        // when: a new host, with a fresh permit, picks the never-run row up. Its first attempt takes the permit and
        // fails, and its immediate retry has none.
        await using var host = await PipelineHosts.RestartAsync(
            stopped,
            probe,
            AbortToken,
            _ConfigureThrottled,
            _ConfigureServices
        );
        await host.RunRetryPickupAsync(_PastRetryDelay, AbortToken);

        // then: the pickup did not wait out the hour, and ran the other consumer's retry
        await host.WithinBoundAsync(probe.WhenCompletedAsync(_Retried, 1), AbortToken);
        probe.Attempts(_WaitingThrottled).Should().Be(1);

        // and: the throttled row is put back unleased with its budget intact, for a later pickup to go on with
        var deferred = (await _ThrottledRowsAsync(host))
            .Should()
            .ContainSingle(static row => row.StatusName == StatusName.Scheduled)
            .Subject;
        deferred.Retries.Should().Be(0, "an immediate retry that did not run spends nothing");
        deferred.NextRetryAt.Should().BeAfter(host.Clock.GetUtcNow());
        deferred.LockedUntil.Should().BeNull();
    }

    private static void _ConfigureThrottled(MessagingSetupBuilder setup)
    {
        setup.AddConsumer<ThrottledPipelineOrderConsumer>();
        setup.DefaultFailurePolicy(policy => policy.Delayed(1, _RetryDelay, _RetryDelay));
        setup.Tune(
            ThrottledPipelineOrderConsumer.Identity,
            consumer =>
                consumer
                    .Concurrency(4)
                    .FailurePolicy(policy => policy.Immediate(2).Delayed(1, _RetryDelay, _RetryDelay))
                    .RateLimit(ConsumerRateLimit.FixedWindow(1, TimeSpan.FromHours(1)))
        );
    }

    private static void _ConfigureServices(IServiceCollection services)
    {
        services.ConfigureMessaging(messaging =>
            messaging.Message<ThrottledPipelineOrder>(ThrottledPipelineOrder.Name)
        );
    }

    private static async Task<IReadOnlyList<MessageView>> _ThrottledRowsAsync(SimulationHost host)
    {
        var page = await host
            .Storage.GetMonitoringApi()
            .GetMessagesAsync(
                new MessageQuery
                {
                    MessageType = MessageType.Subscribe,
                    Name = ThrottledPipelineOrder.Name,
                    CurrentPage = 0,
                    PageSize = 100,
                },
                AbortToken
            );

        return page.Items;
    }

    // Rows change only through the store, so they are read again after each store call, without moving time.
    private static async Task _WaitForThrottledRowAsync(SimulationHost host, Func<MessageView, bool> predicate)
    {
        while (true)
        {
            var mark = host.Storage.CallMark;
            if ((await _ThrottledRowsAsync(host)).Any(predicate))
            {
                return;
            }

            await host.WithinBoundAsync(host.Storage.WhenCallFinishedAsync(mark, AbortToken), AbortToken);
        }
    }

    // Waits are armed only by the code under test, so the clock's arm mark is waited on instead of time.
    private static async Task _WaitForArmedAsync(SimulationHost host, long mark, TimeSpan due)
    {
        while (true)
        {
            var next = host.Clock.WaitMark;
            if (host.Clock.PendingWaitsArmedSince(mark).Contains(due))
            {
                return;
            }

            await host.WithinBoundAsync(host.Clock.WhenWaitArmedAsync(next, AbortToken), AbortToken);
        }
    }

    private static async Task _EnqueueThrottledAsync(SimulationHost host, string id)
    {
        await using var scope = host.Services.CreateAsyncScope();
        await scope
            .ServiceProvider.GetRequiredService<IQueue>()
            .EnqueueAsync(new ThrottledPipelineOrder(id), AbortToken);
    }
}
