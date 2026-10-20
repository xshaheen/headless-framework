// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Testing.Tests;
using Tests.Guarantees;
using Tests.Simulation;

namespace Tests.Shutdown;

/// <summary>
/// Graceful shutdown on the real pipeline, against the simulated broker and a store that outlives the host, as a
/// database outlives a process: what a cut-off attempt leaves behind, and what the restarted host does with it.
/// </summary>
public sealed class GracefulShutdownTests : TestBase
{
    private const string _OrderId = "order-1";

    [Fact]
    [Guarantee("MSG-SHUT-04")]
    [Guarantee("MSG-CONSUME-06")]
    public async Task should_process_a_message_cut_off_by_shutdown_exactly_once_after_restart_without_writing_state()
    {
        // given: the first attempt runs until shutdown cancels it
        var probe = new PipelineProbe
        {
            Behavior = static (_, attempt, cancellationToken) =>
                attempt == 1
                    ? new ValueTask(Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken))
                    : ValueTask.CompletedTask,
        };
        await using var host = await PipelineHosts.StartAsync(probe, AbortToken);
        await host.EnqueueAsync(_OrderId, AbortToken);
        await host.WithinBoundAsync(probe.WhenAttemptsAsync(_OrderId, 1), AbortToken);

        // when: the host stops with the attempt running
        await host.ShutDownAsync(AbortToken);

        // then: the cut-off attempt wrote no state and fired no OnExhausted
        var cutOff = (await host.ReceivedRowsAsync(AbortToken)).Should().ContainSingle().Subject;
        cutOff.StatusName.Should().NotBe(StatusName.Failed).And.NotBe(StatusName.Succeeded);
        cutOff.Retries.Should().Be(0, "a shutdown cancellation is not a failed attempt");
        probe.Exhausted.Should().BeEmpty();

        // and: the restarted host picks the row up once its lease is gone and runs it to success, once
        await using var restarted = await PipelineHosts.RestartAsync(host, probe, AbortToken);
        await restarted.RunRetryPickupAsync(_PastLease, AbortToken);
        await restarted.WithinBoundAsync(probe.WhenCompletedAsync(_OrderId, 1), AbortToken);
        await restarted.RunForAsync(TimeSpan.FromMinutes(2), AbortToken);

        probe.Attempts(_OrderId).Should().Be(2);
        probe.Completions(_OrderId).Should().Be(1);
        probe.Exhausted.Should().BeEmpty();
        (await restarted.ReceivedRowsAsync(AbortToken))
            .Should()
            .ContainSingle()
            .Which.StatusName.Should()
            .Be(StatusName.Succeeded);
    }

    [Fact]
    [Guarantee("MSG-SHUT-05")]
    public async Task should_commit_a_delivery_whose_admission_completes_while_the_host_shuts_down()
    {
        // given: a delivery whose inbox admission is still waiting on the store when shutdown begins
        var probe = new PipelineProbe();
        await using var host = await PipelineHosts.StartAsync(probe, AbortToken);
        var admissionDelay = TimeSpan.FromSeconds(5);
        host.Faults.DelayStoreCommit(StorageOperation.AdmitReceivedMessage, admissionDelay);
        var admissionMark = host.Clock.WaitMark;
        await host.EnqueueAsync(_OrderId, AbortToken);
        await host.WhenWaitArmedAsync(admissionMark, due => due == admissionDelay, AbortToken);

        // when: the host stops, and once shutdown waits on its own bound (due exactly the shutdown timeout ahead, as
        // the clock has not moved), the admission is stored
        var stopMark = host.Clock.WaitMark;
        var stopping = host.StopAsync(AbortToken);
        await host.WhenWaitArmedAsync(stopMark, due => due == _ShutdownTimeout, AbortToken);
        host.Advance(admissionDelay);
        var settling = host.Broker.WaitForEventAsync(
            e => e.Kind is BrokerEventKind.Committed or BrokerEventKind.Rejected,
            AbortToken
        );
        await host.WithinBoundAsync(settling, AbortToken);
        var settlement = await settling;
        await host.FinishShutdownAsync(stopping, stopMark, AbortToken);

        // then: the delivery was committed at the broker, not abandoned to be delivered again
        settlement.Kind.Should().Be(BrokerEventKind.Committed);
        host.Broker.Events.Should().ContainSingle(e => e.Kind == BrokerEventKind.Committed);
        host.Broker.Events.Should()
            .NotContain(e => e.Kind == BrokerEventKind.Rejected || e.Kind == BrokerEventKind.SettlementRefused);
    }

    private static readonly TimeSpan _ShutdownTimeout = TimeSpan.FromSeconds(30);

    // The cut-off attempt keeps its lease until the dispatch timeout runs out; one poll later the row is claimable.
    private static readonly TimeSpan _PastLease = TimeSpan.FromMinutes(5) + PipelineHosts.PollInterval;
}
