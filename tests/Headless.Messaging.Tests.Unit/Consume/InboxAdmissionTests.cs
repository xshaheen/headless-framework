// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Testing.Tests;
using Tests.Guarantees;
using Tests.Simulation;

namespace Tests.Consume;

/// <summary>
/// How a delivery is taken in on the real consume pipeline, against the simulated broker and store: the broker hears
/// a commit only once the inbox row is durable, a store that cannot take the row sends the delivery back, and a
/// delivery the inbox has already admitted runs nothing again.
/// </summary>
public sealed class InboxAdmissionTests : TestBase
{
    private const string _OrderId = "order-1";

    [Fact]
    [Guarantee("MSG-SETTLE-01")]
    public async Task should_commit_a_delivery_only_after_its_inbox_admission_is_stored()
    {
        // given: the admission write reaches the store only once the clock moves five seconds on
        var probe = new PipelineProbe();
        await using var host = await PipelineHosts.StartAsync(probe, AbortToken);
        var admissionDelay = TimeSpan.FromSeconds(5);
        host.Faults.DelayStoreCommit(StorageOperation.AdmitReceivedMessage, admissionDelay);
        var mark = host.Clock.WaitMark;

        // when: the delivery arrives and its admission waits on the store; the clock has not moved, so the admission's
        // wait is the one due exactly its delay ahead
        await host.EnqueueAsync(_OrderId, AbortToken);
        await host.WhenWaitArmedAsync(mark, due => due == admissionDelay, AbortToken);

        // then: nothing is committed while the admission is not durable
        host.Broker.Events.Should().Contain(e => e.Kind == BrokerEventKind.Delivered);
        host.Broker.Events.Should().NotContain(e => e.Kind == BrokerEventKind.Committed);
        probe.Attempts(_OrderId).Should().Be(0);

        // and: the commit follows the stored admission, and the handler runs
        host.Advance(admissionDelay);
        await host.WithinBoundAsync(probe.WhenCompletedAsync(_OrderId, 1), AbortToken);
        host.Broker.Events.Should().ContainSingle(e => e.Kind == BrokerEventKind.Committed);
        (await host.ReceivedRowsAsync(AbortToken)).Should().ContainSingle();
    }

    [Fact]
    [Guarantee("MSG-SETTLE-02")]
    public async Task should_send_a_delivery_back_for_redelivery_when_its_admission_cannot_be_stored()
    {
        // given: the admission write fails on the first delivery and on each of its in-process retries
        var probe = new PipelineProbe();
        await using var host = await PipelineHosts.StartAsync(probe, AbortToken, PipelineHosts.QuietRetryPickup);
        const int failedAdmissions = 4;
        for (var call = 1; call <= failedAdmissions; call++)
        {
            host.Faults.FailStore(StorageOperation.AdmitReceivedMessage, onCall: call);
        }

        // when: the first delivery arrives, then its admission retries and redelivery run on the clock
        await host.EnqueueAsync(_OrderId, AbortToken);
        await host.WithinBoundAsync(
            host.Broker.WaitForEventAsync(e => e.Kind == BrokerEventKind.Delivered, AbortToken),
            AbortToken
        );
        await host.StepShortWaitsUntilAsync(probe.WhenCompletedAsync(_OrderId, 1), AbortToken);

        // then: the first delivery was rejected without a commit, and only its redelivery was admitted and committed
        host.Storage.AppliedFaults.Should().HaveCount(failedAdmissions);
        var settlements = host
            .Broker.Events.Where(e => e.Kind is BrokerEventKind.Committed or BrokerEventKind.Rejected)
            .ToList();
        settlements.Should().HaveCount(2);
        settlements[0].Should().Match<BrokerEvent>(e => e.Kind == BrokerEventKind.Rejected && e.DeliveryCount == 1);
        settlements[1].Should().Match<BrokerEvent>(e => e.Kind == BrokerEventKind.Committed && e.DeliveryCount == 2);
        probe.Attempts(_OrderId).Should().Be(1, "a delivery the store never admitted ran no attempt");
        (await host.ReceivedRowsAsync(AbortToken)).Should().ContainSingle();
    }

    [Fact]
    [Guarantee("MSG-INBOX-04")]
    public async Task should_run_the_handler_once_and_commit_the_duplicate_when_an_admitted_message_is_delivered_again()
    {
        // given: two direct sends under one message ID reach the broker as two deliveries
        var probe = new PipelineProbe();
        await using var host = await PipelineHosts.StartAsync(probe, AbortToken);
        var options = new QueueOptions { MessageId = "duplicate-1", DeliveryMode = DeliveryMode.Direct };

        // when
        await host.EnqueueAsync(_OrderId, options, AbortToken);
        await host.WithinBoundAsync(probe.WhenCompletedAsync(_OrderId, 1), AbortToken);
        await host.EnqueueAsync(_OrderId, options, AbortToken);
        await host.WithinBoundAsync(
            host.Broker.WaitForEventsAsync(e => e.Kind == BrokerEventKind.Committed, 2, AbortToken),
            AbortToken
        );

        // then: the inbox admits one; the duplicate is committed without a handler run, a poison row, or a dead letter
        probe.Attempts(_OrderId).Should().Be(1);
        host.Storage.CallsFinished(StorageOperation.StoreReceivedExceptionMessage).Should().Be(0);
        host.Broker.Events.Should().NotContain(e => e.Kind == BrokerEventKind.DeadLettered);
        probe.Exhausted.Should().BeEmpty();
        (await host.ReceivedRowsAsync(AbortToken)).Should().ContainSingle();
    }
}
