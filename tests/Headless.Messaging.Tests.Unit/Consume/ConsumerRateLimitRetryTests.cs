// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
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
                id == _Retried && attempt == 1 ? throw new InvalidOperationException("boom") : ValueTask.CompletedTask,
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

        // when: one pickup claims the waiting delivery's row, past its dispatch grace, and the due retry behind it
        await host.RunRetryPickupAsync(_PastRetryDelay, AbortToken);

        // then: the retry ran, and the pickup finished without the throttled row
        await host.WithinBoundAsync(probe.WhenCompletedAsync(_Retried, 1), AbortToken);
        probe.Attempts(_Retried).Should().Be(2);
        probe.Attempts(_WaitingThrottled).Should().Be(0);

        // and: the throttled row is put back unleased for its own delivery, due again later
        var throttledRows = await host
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
        var waiting = throttledRows.Items.Should().ContainSingle(row => row.StatusName == StatusName.Scheduled).Subject;
        waiting.NextRetryAt.Should().BeAfter(host.Clock.GetUtcNow());
        waiting.LockedUntil.Should().BeNull();
    }

    private static async Task _EnqueueThrottledAsync(SimulationHost host, string id)
    {
        await using var scope = host.Services.CreateAsyncScope();
        await scope
            .ServiceProvider.GetRequiredService<IQueue>()
            .EnqueueAsync(new ThrottledPipelineOrder(id), AbortToken);
    }
}
