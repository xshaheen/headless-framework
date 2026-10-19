// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Testing.Tests;
using Tests.Guarantees;
using Tests.Simulation;

namespace Tests.Consume;

/// <summary>
/// A failing handler on the real consume pipeline, against the simulated broker and store: how many attempts it gets,
/// how its row ends, and how often <c>OnExhausted</c> fires.
/// </summary>
public sealed class ConsumeFailurePolicyTests : TestBase
{
    private const string _OrderId = "order-1";
    private static readonly TimeSpan _RetryDelay = TimeSpan.FromSeconds(10);

    // A delayed retry falls due within its delay plus 20% jitter, and retry pickup sees it on its next poll.
    private static readonly TimeSpan _PastRetryDelay = (_RetryDelay * 1.2) + PipelineHosts.PollInterval;

    [Fact]
    [Guarantee("MSG-CONSUME-01")]
    public async Task should_retry_a_throwing_handler_per_policy_then_leave_its_row_terminal_and_fire_on_exhausted_once()
    {
        // given: one immediate and two delayed retries, so four attempts in all
        var probe = new PipelineProbe { Behavior = static (_, _, _) => throw new InvalidOperationException("boom") };
        await using var host = await PipelineHosts.StartAsync(
            probe,
            AbortToken,
            static setup =>
                setup.DefaultFailurePolicy(policy => policy.Immediate(1).Delayed(2, _RetryDelay, _RetryDelay))
        );

        // when: the first dispatch runs the first attempt and the immediate retry, then schedules a delayed retry
        await host.EnqueueAsync(_OrderId, AbortToken);
        await host.WaitForRowAsync(_IsRetryScheduled(host), AbortToken);
        probe.Attempts(_OrderId).Should().Be(2);

        // and: each delayed retry runs once the clock passes its delay
        await host.RunRetryPickupAsync(_PastRetryDelay, AbortToken);
        probe.Attempts(_OrderId).Should().Be(3);
        await host.WaitForRowAsync(_IsRetryScheduled(host), AbortToken);

        await host.RunRetryPickupAsync(_PastRetryDelay, AbortToken);
        await host.WithinBoundAsync(probe.WhenExhaustedAsync(1), AbortToken);

        // then
        probe.Attempts(_OrderId).Should().Be(4);
        var row = (await host.ReceivedRowsAsync(AbortToken)).Should().ContainSingle().Subject;
        row.StatusName.Should().Be(StatusName.Failed);
        row.NextRetryAt.Should().BeNull("a spent policy leaves no retry scheduled");
        probe.Exhausted.Should().ContainSingle().Which.StorageId.Should().Be(row.StorageId);

        // and: nothing runs it again, and OnExhausted does not fire twice
        await host.RunForAsync(TimeSpan.FromMinutes(2), AbortToken);
        probe.Attempts(_OrderId).Should().Be(4);
        probe.Exhausted.Should().ContainSingle();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Guarantee("MSG-CONSUME-03")]
    public async Task should_fail_the_row_at_once_and_fire_on_exhausted_once_when_a_fail_rule_or_permanent_exception_matches(
        bool permanent
    )
    {
        // given: a policy with retries left, and a failure that must end it at once: one its fail rule names, or one in
        // the built-in permanent set
        Exception failure = permanent
            ? new ArgumentException("bad order", nameof(permanent))
            : new OrderRejectedException();
        var probe = new PipelineProbe { Behavior = (_, _, _) => throw failure };
        await using var host = await PipelineHosts.StartAsync(
            probe,
            AbortToken,
            static setup =>
                setup.DefaultFailurePolicy(policy =>
                    policy.Immediate(2).Delayed(2, _RetryDelay, _RetryDelay).FailOn<OrderRejectedException>()
                )
        );

        // when
        await host.EnqueueAsync(_OrderId, AbortToken);
        await host.WithinBoundAsync(probe.WhenExhaustedAsync(1), AbortToken);

        // then
        probe.Attempts(_OrderId).Should().Be(1);
        var row = (await host.ReceivedRowsAsync(AbortToken)).Should().ContainSingle().Subject;
        row.StatusName.Should().Be(StatusName.Failed);
        row.NextRetryAt.Should().BeNull();

        await host.RunForAsync(TimeSpan.FromMinutes(1), AbortToken);
        probe.Attempts(_OrderId).Should().Be(1);
        probe.Exhausted.Should().ContainSingle();
    }

    [Fact]
    [Guarantee("MSG-CONSUME-02")]
    public async Task should_end_the_row_as_spent_without_running_the_handler_after_a_crash_during_its_final_attempt()
    {
        // given: two attempts in all, and the second, final one runs until the host goes down
        var probe = new PipelineProbe
        {
            Behavior = static (_, attempt, cancellationToken) =>
                attempt == 1
                    ? throw new InvalidOperationException("boom")
                    : new ValueTask(Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)),
        };
        static void policy(MessagingSetupBuilder setup) => setup.DefaultFailurePolicy(policy => policy.Immediate(1));
        await using var host = await PipelineHosts.StartAsync(probe, AbortToken, policy);
        await host.EnqueueAsync(_OrderId, AbortToken);
        await host.WithinBoundAsync(probe.WhenAttemptsAsync(_OrderId, 2), AbortToken);

        // when: the process stops mid-attempt, after the final attempt was reserved, and comes back
        await host.ShutDownAsync(AbortToken);
        await using var restarted = await PipelineHosts.RestartAsync(host, probe, AbortToken, policy);
        await restarted.RunRetryPickupAsync(_PastLease, AbortToken);
        await restarted.WithinBoundAsync(probe.WhenExhaustedAsync(1), AbortToken);

        // then: the restarted host spent nothing more on it
        probe.Attempts(_OrderId).Should().Be(2, "the budget was spent before the crash");
        var row = (await restarted.ReceivedRowsAsync(AbortToken)).Should().ContainSingle().Subject;
        row.StatusName.Should().Be(StatusName.Failed);
        row.NextRetryAt.Should().BeNull();
        probe.Exhausted.Should().ContainSingle();
    }

    [Fact]
    [Guarantee("MSG-CONSUME-04")]
    public async Task should_stop_without_on_exhausted_or_a_breaker_failure_when_the_store_refuses_the_terminal_write()
    {
        // given: a transient failure the fail rule ends at once, and a terminal write the store refuses because another
        // writer already made the row terminal
        var probe = new PipelineProbe
        {
            Behavior = static (_, _, _) => throw new TimeoutException("dependency timed out"),
        };
        await using var host = await PipelineHosts.StartAsync(
            probe,
            AbortToken,
            static setup =>
                setup.DefaultFailurePolicy(policy =>
                    policy.Immediate(2).Delayed(2, _RetryDelay, _RetryDelay).FailOn<TimeoutException>()
                )
        );
        host.Faults.RefuseStoreWrite(StorageOperation.ChangeReceiveRetryState);

        // when
        await host.EnqueueAsync(_OrderId, AbortToken);
        await host.WithinBoundAsync(probe.WhenAttemptsAsync(_OrderId, 1), AbortToken);
        await host.WaitForCallsAsync(StorageOperation.ChangeReceiveRetryState, 1, AbortToken);
        await host.RunForAsync(TimeSpan.FromSeconds(5), AbortToken);

        // then: the losing writer stops: no OnExhausted, no breaker failure, and no further attempt
        host.Storage.AppliedFaults.Should().ContainSingle().Which.Fault.Kind.Should().Be(StoreFaultKind.Refuse);
        probe.Exhausted.Should().BeEmpty();
        probe.Attempts(_OrderId).Should().Be(1);
        host.ConsumerCircuit().ConsecutiveFailures.Should().Be(0);
    }

    [Fact]
    [Guarantee("MSG-CONSUME-05")]
    public async Task should_not_run_a_succeeded_handler_again_inline_when_its_success_write_fails()
    {
        // given: immediate retries are allowed, and the success write of the first attempt fails
        var probe = new PipelineProbe();
        await using var host = await PipelineHosts.StartAsync(
            probe,
            AbortToken,
            static setup => setup.DefaultFailurePolicy(policy => policy.Immediate(2))
        );
        host.Faults.FailStore(StorageOperation.ChangeReceiveRetryState);

        // when
        await host.EnqueueAsync(_OrderId, AbortToken);
        await host.WithinBoundAsync(probe.WhenCompletedAsync(_OrderId, 1), AbortToken);
        await host.WaitForCallsAsync(StorageOperation.ChangeReceiveRetryState, 1, AbortToken);
        await host.RunForAsync(TimeSpan.FromSeconds(5), AbortToken);

        // then: the failed write is no consumer failure: no inline retry, no OnExhausted, no breaker failure
        host.Storage.AppliedFaults.Should().ContainSingle().Which.Fault.Kind.Should().Be(StoreFaultKind.Fail);
        probe.Attempts(_OrderId).Should().Be(1);
        probe.Exhausted.Should().BeEmpty();
        host.ConsumerCircuit().ConsecutiveFailures.Should().Be(0);
    }

    // A cut-off attempt keeps its lease until the dispatch timeout runs out; one poll later the row is claimable.
    private static readonly TimeSpan _PastLease = TimeSpan.FromMinutes(5) + PipelineHosts.PollInterval;

    private sealed class OrderRejectedException() : Exception("The order was rejected.");

    /// <summary>A failed row whose next retry lies ahead of the clock, so a retry scheduled earlier does not match.</summary>
    private static Func<MessageView, bool> _IsRetryScheduled(SimulationHost host) =>
        row => row.StatusName == StatusName.Failed && row.NextRetryAt > host.Clock.GetUtcNow();
}
