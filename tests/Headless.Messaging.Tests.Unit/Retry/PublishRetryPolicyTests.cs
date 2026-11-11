// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Polly;
using Tests.Guarantees;
using Tests.Simulation;

namespace Tests.Retry;

/// <summary>
/// A failing publish on the real outbox pipeline, against the simulated broker and store: how many sends it gets, how
/// a crash or a shutdown in the middle of a send leaves its row, and how a send the broker never confirms is retried.
/// </summary>
public sealed class PublishRetryPolicyTests : TestBase
{
    private const string _OrderId = "order-1";
    private const StorageOperation _RelayClaim = StorageOperation.GetPublishedMessagesOfNeedRetry;

    // Past the default publish lease, which a send cut off by a crash keeps until it expires.
    private static readonly TimeSpan _PastLease = TimeSpan.FromMinutes(5) + PipelineHosts.PollInterval;

    [Fact]
    [Guarantee("MSG-RETRY-04")]
    public async Task should_spend_the_inline_budget_on_each_pickup_then_end_the_row_failed_and_fire_on_terminal_once()
    {
        // given: two sends per pickup and two persisted retries, so (1 + 1) x (2 + 1) = 6 sends in all; the broker
        // refuses more sends than that
        var probe = new PipelineProbe();
        await using var host = await PipelineHosts.StartAsync(
            probe,
            AbortToken,
            static setup => _PublishPolicy(setup, inlineRetries: 1, persistedRetries: 2, backoff: TimeSpan.Zero)
        );
        host.Faults.FailPublishes(20);

        // when: the first dispatch runs its burst, then persists the first retry
        await host.EnqueueAsync(_OrderId, AbortToken);
        var afterDispatch = await host.WaitForPublishedRowAsync(row => row.Retries == 1, AbortToken);

        // then: each burst spent exactly the inline budget, and only the persisted retry count moved
        _Sends(host, BrokerEventKind.PublishFailed).Should().Be(2);
        afterDispatch.StatusName.Should().Be(StatusName.Failed);
        afterDispatch.NextRetryAt.Should().NotBeNull("a retry is persisted while the budget lasts");

        await host.RunRetryPickupAsync(PipelineHosts.PollInterval, AbortToken, _RelayClaim);
        _Sends(host, BrokerEventKind.PublishFailed).Should().Be(4);
        (await host.PublishedRowsAsync(AbortToken)).Should().ContainSingle().Which.Retries.Should().Be(2);

        await host.RunRetryPickupAsync(PipelineHosts.PollInterval, AbortToken, _RelayClaim);
        await host.WithinBoundAsync(probe.WhenExhaustedAsync(1), AbortToken);

        // and: the spent budget ends the row Failed with no retry, and OnTerminal reports the exhausted publish
        _Sends(host, BrokerEventKind.PublishFailed).Should().Be(6);
        var row = (await host.PublishedRowsAsync(AbortToken)).Should().ContainSingle().Subject;
        row.StatusName.Should().Be(StatusName.Failed);
        row.NextRetryAt.Should().BeNull("a spent budget leaves no retry scheduled");
        row.Retries.Should().Be(2);
        var terminal = probe.Exhausted.Should().ContainSingle().Subject;
        terminal.MessageType.Should().Be(MessageType.Publish);
        terminal.Reason.Should().Be(MessageTerminalReason.RetriesExhausted);
        terminal.StorageId.Should().Be(row.StorageId);

        // and: nothing sends it again, and OnTerminal does not fire twice
        await host.RunForAsync(TimeSpan.FromMinutes(2), AbortToken);
        _Sends(host, BrokerEventKind.PublishFailed).Should().Be(6);
        _Sends(host, BrokerEventKind.Published).Should().Be(0);
        probe.Exhausted.Should().ContainSingle();
    }

    [Fact]
    [Guarantee("MSG-RETRY-05")]
    public async Task should_not_send_again_when_recovering_a_row_whose_crashed_dispatch_reserved_the_whole_inline_budget()
    {
        // given: two sends per pickup and no persisted retry; the first send fails and the second, final one hangs
        var probe = new PipelineProbe();
        using var lifetime = new StoppingLifetime();
        static void policy(MessagingSetupBuilder setup) =>
            _PublishPolicy(setup, inlineRetries: 1, persistedRetries: 0, backoff: TimeSpan.Zero);
        await using var host = await PipelineHosts.StartAsync(
            probe,
            AbortToken,
            policy,
            configureServices: services => services.AddSingleton<IHostApplicationLifetime>(lifetime)
        );
        host.Faults.FailPublishes(1).HangPublishes(1);

        await host.EnqueueAsync(_OrderId, AbortToken);
        await host.WaitUntilAsync(() => _Sends(host, BrokerEventKind.PublishHung) == 1, AbortToken);
        var storageId = (await host.PublishedRowsAsync(AbortToken)).Should().ContainSingle().Subject.StorageId;
        var reserved = await host.Storage.GetMonitoringApi().GetPublishedMessageAsync(storageId, AbortToken);
        reserved!.InlineAttempts.Should().Be(2, "the hung send is the last one the inline budget allows");

        // when: the process stops while the final send is in flight, writing nothing, and comes back
        lifetime.StopApplication();
        await host.ShutDownAsync(AbortToken);
        await using var restarted = await PipelineHosts.RestartAsync(host, probe, AbortToken, policy);
        await restarted.RunRetryPickupAsync(_PastLease, AbortToken, _RelayClaim);
        await restarted.WithinBoundAsync(probe.WhenExhaustedAsync(1), AbortToken);

        // then: recovery ended the row as spent without another send
        _Sends(restarted, BrokerEventKind.PublishFailed).Should().Be(1);
        _Sends(restarted, BrokerEventKind.PublishHung).Should().Be(1);
        _Sends(restarted, BrokerEventKind.Published).Should().Be(0, "the reserved budget was spent before the crash");
        var row = (await restarted.PublishedRowsAsync(AbortToken)).Should().ContainSingle().Subject;
        row.StatusName.Should().Be(StatusName.Failed);
        row.NextRetryAt.Should().BeNull();
        probe.Exhausted.Should().ContainSingle().Which.Reason.Should().Be(MessageTerminalReason.RetriesExhausted);

        await restarted.RunForAsync(TimeSpan.FromMinutes(1), AbortToken);
        _Sends(restarted, BrokerEventKind.Published).Should().Be(0);
        probe.Exhausted.Should().ContainSingle();
    }

    [Fact]
    [Guarantee("MSG-RETRY-06")]
    public async Task should_record_a_failed_attempt_and_back_off_when_the_broker_does_not_confirm_a_send_in_time()
    {
        // given: one send per pickup, a fixed five-second back-off, and a broker that never confirms the first send
        var backoff = TimeSpan.FromSeconds(5);
        var probe = new PipelineProbe();
        await using var host = await PipelineHosts.StartAsync(
            probe,
            AbortToken,
            setup => _PublishPolicy(setup, inlineRetries: 0, persistedRetries: 3, backoff)
        );
        host.Faults.HangPublishes(1);
        var publishTimeout = host.GetRequiredService<IOptions<MessagingOptions>>().Value.TransportPublishTimeout;

        await host.EnqueueAsync(_OrderId, AbortToken);
        await host.WaitUntilAsync(() => _Sends(host, BrokerEventKind.PublishHung) == 1, AbortToken);

        // when: the publish timeout passes while the host keeps running
        host.Advance(publishTimeout);
        var failed = await host.WaitForPublishedRowAsync(row => row.Retries == 1, AbortToken);

        // then: the timed-out send is a failed attempt, retried only once its back-off has passed
        failed.StatusName.Should().Be(StatusName.Failed);
        failed.NextRetryAt.Should().Be(host.Clock.GetUtcNow() + backoff);
        probe.Exhausted.Should().BeEmpty();

        for (var second = 1; second < backoff.TotalSeconds; second++)
        {
            await host.RunRetryPickupAsync(PipelineHosts.PollInterval, AbortToken, _RelayClaim);
        }

        _Sends(host, BrokerEventKind.Published).Should().Be(0, "the back-off has not passed yet");

        await host.RunRetryPickupAsync(PipelineHosts.PollInterval, AbortToken, _RelayClaim);
        _Sends(host, BrokerEventKind.Published).Should().Be(1);
        (await host.PublishedRowsAsync(AbortToken))
            .Should()
            .ContainSingle()
            .Which.StatusName.Should()
            .Be(StatusName.Succeeded);
    }

    [Fact]
    [Guarantee("MSG-RETRY-06")]
    public async Task should_write_nothing_when_shutdown_cancels_a_send_and_send_it_once_after_the_restart()
    {
        // given: one send per pickup, and a broker that never confirms the first send
        var probe = new PipelineProbe();
        using var lifetime = new StoppingLifetime();
        static void policy(MessagingSetupBuilder setup) =>
            _PublishPolicy(setup, inlineRetries: 0, persistedRetries: 3, backoff: TimeSpan.FromSeconds(5));
        await using var host = await PipelineHosts.StartAsync(
            probe,
            AbortToken,
            policy,
            configureServices: services => services.AddSingleton<IHostApplicationLifetime>(lifetime)
        );
        host.Faults.HangPublishes(1);

        await host.EnqueueAsync(_OrderId, AbortToken);
        await host.WaitUntilAsync(() => _Sends(host, BrokerEventKind.PublishHung) == 1, AbortToken);
        var before = (await host.PublishedRowsAsync(AbortToken)).Should().ContainSingle().Subject;
        var stateWrites = host.Storage.CallsFinished(StorageOperation.ChangePublishRetryState);

        // when: host shutdown cancels the send
        lifetime.StopApplication();
        await host.ShutDownAsync(AbortToken);

        // then: the cancelled send wrote no state and spent no retry
        host.Storage.CallsFinished(StorageOperation.ChangePublishRetryState).Should().Be(stateWrites);
        var after = (await host.PublishedRowsAsync(AbortToken)).Should().ContainSingle().Subject;
        after.StatusName.Should().Be(before.StatusName);
        after.Retries.Should().Be(0);
        after.NextRetryAt.Should().Be(before.NextRetryAt);
        probe.Exhausted.Should().BeEmpty();

        // and: the restarted host sends it once
        await using var restarted = await PipelineHosts.RestartAsync(host, probe, AbortToken, policy);
        await restarted.RunRetryPickupAsync(_PastLease, AbortToken, _RelayClaim);
        await restarted.RunForAsync(TimeSpan.FromSeconds(30), AbortToken);
        _Sends(restarted, BrokerEventKind.Published).Should().Be(1);
        (await restarted.PublishedRowsAsync(AbortToken))
            .Should()
            .ContainSingle()
            .Which.StatusName.Should()
            .Be(StatusName.Succeeded);
    }

    // A fixed back-off without jitter, so a test can step to the exact moment a persisted retry falls due.
    private static void _PublishPolicy(
        MessagingSetupBuilder setup,
        int inlineRetries,
        int persistedRetries,
        TimeSpan backoff
    )
    {
        var policy = setup.Options.RetryPolicy;
        policy.RetryStrategy.MaxRetryAttempts = inlineRetries;
        policy.RetryStrategy.Delay = backoff;
        policy.RetryStrategy.BackoffType = DelayBackoffType.Constant;
        policy.RetryStrategy.UseJitter = false;
        policy.MaxPersistedRetries = persistedRetries;
    }

    private static int _Sends(SimulationHost host, BrokerEventKind kind) =>
        host.Broker.Events.Count(e => e.Kind == kind);

    /// <summary>A host lifetime the test stops, which is how the pipeline tells host shutdown from a slow broker.</summary>
    private sealed class StoppingLifetime : IHostApplicationLifetime, IDisposable
    {
        private readonly CancellationTokenSource _stopping = new();

        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => _stopping.Token;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication() => _stopping.Cancel();

        public void Dispose() => _stopping.Dispose();
    }
}
