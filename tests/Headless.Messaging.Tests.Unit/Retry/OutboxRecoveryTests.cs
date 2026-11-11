// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Testing.Tests;
using Polly;
using Tests.Guarantees;
using Tests.Simulation;

namespace Tests.Retry;

/// <summary>
/// What recovers an outbox row and a delivery when the fast path is lost, on the real pipeline against the simulated
/// broker and store: relay pickup sends a committed row nobody signalled, and a transient storage outage on the way to
/// a send or an admission costs no row and no retry.
/// </summary>
public sealed class OutboxRecoveryTests : TestBase
{
    private const string _OrderId = "order-1";
    private const StorageOperation _RelayClaim = StorageOperation.GetPublishedMessagesOfNeedRetry;

    // Past the default initial dispatch grace, which keeps relay pickup off a row the dispatcher may still be sending.
    private static readonly TimeSpan _Grace = TimeSpan.FromSeconds(30);

    // Past the default publish lease, which a reservation that never answered may still hold.
    private static readonly TimeSpan _PastLease = TimeSpan.FromMinutes(5) + PipelineHosts.PollInterval;

    // The signal is lost the way a crash between the commit and the signal loses it: the commit lands, its reply does
    // not, so the publisher never hands the row to the dispatcher.
    [Fact]
    [Guarantee("MSG-PUB-08")]
    public async Task should_send_a_committed_row_by_relay_pickup_when_its_dispatch_signal_is_lost()
    {
        // given: the outbox write commits, but the publisher sees it fail
        var probe = new PipelineProbe();
        await using var host = await PipelineHosts.StartAsync(probe, AbortToken);
        host.Faults.LoseStoreReply(StorageOperation.StoreMessage);

        // when
        var enqueue = () => host.EnqueueAsync(_OrderId, AbortToken);

        // then: the publisher saw a failure, and the store kept exactly the row it committed
        await enqueue.Should().ThrowAsync<SimulatedStoreException>();
        var stored = (await host.PublishedRowsAsync(AbortToken)).Should().ContainSingle().Subject;
        stored.StatusName.Should().Be(StatusName.Scheduled);

        // and: nothing sends it during the dispatch grace, since no signal reached the dispatcher
        await host.RunForAsync(_Grace - PipelineHosts.PollInterval, AbortToken);
        _Sends(host, BrokerEventKind.Published).Should().Be(0);

        // and: relay pickup sends it once the grace has passed, and only once
        await host.RunRetryPickupAsync(PipelineHosts.PollInterval * 2, AbortToken, _RelayClaim);
        await host.WithinBoundAsync(probe.WhenCompletedAsync(_OrderId, 1), AbortToken);
        _Sends(host, BrokerEventKind.Published).Should().Be(1);
        (await host.PublishedRowsAsync(AbortToken))
            .Should()
            .ContainSingle()
            .Which.StatusName.Should()
            .Be(StatusName.Succeeded);

        await host.RunForAsync(TimeSpan.FromMinutes(1), AbortToken);
        _Sends(host, BrokerEventKind.Published).Should().Be(1);
        probe.Attempts(_OrderId).Should().Be(1);
    }

    [Fact]
    [Guarantee("MSG-RETRY-10")]
    public async Task should_send_the_row_with_its_whole_budget_when_the_store_fails_the_first_dispatch_reservation()
    {
        // given: a budget of one send in all, and a store that fails the first dispatch's lease-and-reserve write
        var probe = new PipelineProbe();
        await using var host = await PipelineHosts.StartAsync(
            probe,
            AbortToken,
            static setup => _PublishPolicy(setup, persistedRetries: 0)
        );
        host.Faults.FailStore(StorageOperation.LeasePublishAndReserveAttempt);

        // when: the dispatch's reservation fails, then relay pickup takes the row once it is due
        await host.EnqueueAsync(_OrderId, AbortToken);
        await host.WaitForCallsAsync(StorageOperation.LeasePublishAndReserveAttempt, 1, AbortToken);
        await host.RunRetryPickupAsync(_PastLease, AbortToken, _RelayClaim);
        await host.RunForAsync(TimeSpan.FromSeconds(5), AbortToken);

        // then: the outage cost neither the row nor its only send
        host.Storage.AppliedFaults.Should().ContainSingle().Which.Fault.Kind.Should().Be(StoreFaultKind.Fail);
        _Sends(host, BrokerEventKind.Published).Should().Be(1);
        var row = (await host.PublishedRowsAsync(AbortToken)).Should().ContainSingle().Subject;
        row.StatusName.Should().Be(StatusName.Succeeded);
        row.Retries.Should().Be(0);
        probe.Exhausted.Should().BeEmpty();
    }

    [Fact]
    [Guarantee("MSG-RETRY-10")]
    public async Task should_send_the_row_with_its_last_retry_when_the_store_fails_the_retry_claim_and_reservation()
    {
        // given: two sends in all; the first fails, so the row waits for its one persisted retry
        var probe = new PipelineProbe();
        await using var host = await PipelineHosts.StartAsync(
            probe,
            AbortToken,
            static setup => _PublishPolicy(setup, persistedRetries: 1)
        );
        host.Faults.FailPublishes(1);
        await host.EnqueueAsync(_OrderId, AbortToken);
        await host.WaitForPublishedRowAsync(row => row.Retries == 1, AbortToken);

        // when: the store fails the next relay claim, then the reservation of the claimed retry
        host.Faults.FailStore(_RelayClaim).FailStore(StorageOperation.ReservePublishAttempt);
        await host.RunRetryPickupAsync(PipelineHosts.PollInterval, AbortToken, _RelayClaim);
        await host.RunRetryPickupAsync(PipelineHosts.PollInterval, AbortToken, _RelayClaim);
        await host.RunRetryPickupAsync(_PastLease, AbortToken, _RelayClaim);
        await host.RunForAsync(TimeSpan.FromSeconds(5), AbortToken);

        // then: both faults fired, and the last retry was still there to send the row
        host.Storage.AppliedFaults.Select(applied => applied.Fault.Operation)
            .Should()
            .BeEquivalentTo([_RelayClaim, StorageOperation.ReservePublishAttempt]);
        _Sends(host, BrokerEventKind.PublishFailed).Should().Be(1);
        _Sends(host, BrokerEventKind.Published).Should().Be(1);
        var row = (await host.PublishedRowsAsync(AbortToken)).Should().ContainSingle().Subject;
        row.StatusName.Should().Be(StatusName.Succeeded);
        row.Retries.Should().Be(1);
        probe.Exhausted.Should().BeEmpty();
    }

    [Fact]
    [Guarantee("MSG-RETRY-10")]
    public async Task should_run_the_handler_once_with_no_retry_spent_when_the_store_fails_its_inbox_admission()
    {
        // given: a consumer with no retry at all, and a store that fails the delivery's admission and each of its
        // in-process admission retries, so the delivery goes back to the broker
        var probe = new PipelineProbe();
        await using var host = await PipelineHosts.StartAsync(
            probe,
            AbortToken,
            static setup =>
            {
                PipelineHosts.QuietRetryPickup(setup);
                setup.DefaultFailurePolicy(static _ => { });
            }
        );
        const int failedAdmissions = 4;
        for (var call = 1; call <= failedAdmissions; call++)
        {
            host.Faults.FailStore(StorageOperation.AdmitReceivedMessage, onCall: call);
        }

        // when
        await host.EnqueueAsync(_OrderId, AbortToken);
        await host.WithinBoundAsync(
            host.Broker.WaitForEventAsync(e => e.Kind == BrokerEventKind.Delivered, AbortToken),
            AbortToken
        );
        await host.StepShortWaitsUntilAsync(probe.WhenCompletedAsync(_OrderId, 1), AbortToken);

        // then: the outage cost neither the delivery nor an attempt: the redelivery ran the only attempt, and it succeeded
        host.Storage.AppliedFaults.Should().HaveCount(failedAdmissions);
        host.Broker.Events.Should().Contain(e => e.Kind == BrokerEventKind.Rejected && e.DeliveryCount == 1);
        probe.Attempts(_OrderId).Should().Be(1);
        var row = await host.WaitForRowAsync(row => row.StatusName == StatusName.Succeeded, AbortToken);
        row.Retries.Should().Be(0);
        probe.Exhausted.Should().BeEmpty();
        host.ConsumerCircuit().ConsecutiveFailures.Should().Be(0);
    }

    // One send per pickup with no back-off, so a persisted retry is due on the next poll.
    private static void _PublishPolicy(MessagingSetupBuilder setup, int persistedRetries)
    {
        var policy = setup.Options.RetryPolicy;
        policy.RetryStrategy.MaxRetryAttempts = 0;
        policy.RetryStrategy.Delay = TimeSpan.Zero;
        policy.RetryStrategy.BackoffType = DelayBackoffType.Constant;
        policy.RetryStrategy.UseJitter = false;
        policy.MaxPersistedRetries = persistedRetries;
    }

    private static int _Sends(SimulationHost host, BrokerEventKind kind) =>
        host.Broker.Events.Count(e => e.Kind == kind);
}
