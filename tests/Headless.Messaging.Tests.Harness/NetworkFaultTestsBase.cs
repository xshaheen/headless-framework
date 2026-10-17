// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Tests.Capabilities;
using Tests.Guarantees;

namespace Tests;

/// <summary>
/// The network path between a client and its broker, which a test can cut while the broker keeps running. A Toxiproxy
/// route implements it against a real broker, and the simulator by dropping its connection on cue.
/// </summary>
[PublicAPI]
public interface INetworkLink
{
    /// <summary>Cuts every open connection and refuses new ones until <see cref="RestoreAsync"/>.</summary>
    Task CutAsync(CancellationToken cancellationToken = default);

    /// <summary>Accepts connections again.</summary>
    Task RestoreAsync(CancellationToken cancellationToken = default);
}

/// <summary>A conformance session whose client reaches the broker over a link the test can cut.</summary>
[PublicAPI]
public sealed class NetworkFaultSession(
    TransportConsumerConformanceSession session,
    INetworkLink link,
    Func<ValueTask> disposeResources
) : IAsyncDisposable
{
    public TransportConsumerConformanceSession Session { get; } = session;

    public INetworkLink Link { get; } = link;

    public async ValueTask DisposeAsync()
    {
        try
        {
            await Link.RestoreAsync().ConfigureAwait(false);
        }
#pragma warning disable CA1031, ERP022 // Cleanup must reach the session and resource disposal even when the link is gone.
        catch (Exception)
        {
            // The link may already be torn down with its container.
        }
#pragma warning restore CA1031, ERP022

        try
        {
            await Session.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            await disposeResources().ConfigureAwait(false);
        }
    }
}

/// <summary>
/// Network-fault invariants for broker-backed transports: the broker stays healthy and only the link fails, which
/// is what a connection blip, a load balancer reset, or a cloud network event looks like to a client.
/// </summary>
/// <remarks>
/// Every window and pause runs on the session's clock, so a simulator leaf runs the same body in simulated time.
/// </remarks>
[PublicAPI]
public abstract class NetworkFaultTestsBase : TransportConsumerConformanceSessionTestsBase
{
    // Long enough for a client that routes around the link to fetch and deliver what was published past it.
    private static readonly TimeSpan _CutObservationWindow = TimeSpan.FromSeconds(10);

    /// <summary>How long a client may take to reconnect and deliver again once the network is back.</summary>
    protected virtual TimeSpan RecoveryWindow => TimeSpan.FromSeconds(60);

    /// <summary>How long the network stays down while publishes are attempted.</summary>
    protected virtual TimeSpan OutageDuration => TimeSpan.FromSeconds(15);

    /// <summary>
    /// How long the client reconnects before the first publish. A publish during recovery can itself rebuild the
    /// connection, so the drop scenario stays quiet to tell a client that cannot reconnect from one a publish broke.
    /// Reconnect back-off runs on the client's clock, so this is a wait on the client, not on the test.
    /// </summary>
    protected virtual TimeSpan QuietReconnectPeriod => TimeSpan.FromSeconds(20);

    /// <summary>
    /// Opens a session whose producer and consumer reach the broker over a link the test can cut. The returned value
    /// owns the link and any broker or proxy it started.
    /// </summary>
    protected abstract ValueTask<NetworkFaultSession> CreateNetworkFaultSessionAsync(
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Every open connection is cut once and nothing publishes while the client reconnects; it must then consume and
    /// publish again.
    /// </summary>
    [Guarantee("MSG-CONN-02")]
    public virtual async Task should_resume_delivery_once_after_the_connection_is_dropped()
    {
        RequireSupport(TransportConformanceScenario.ConnectionDropRecovery);

        await using var faulted = await CreateNetworkFaultSessionAsync(AbortToken);
        var session = faulted.Session;
        await session.StartAsync(cancellationToken: AbortToken);
        await _ProveLiveAsync(session, "before-drop");

        await faulted.Link.CutAsync(AbortToken);
        await faulted.Link.RestoreAsync(AbortToken);
        await session.TimeProvider.Delay(QuietReconnectPeriod, AbortToken);

        var after = _CreateProbe(session.Destination, "after-drop");
        await session.PublishUntilSucceededAsync(after, RecoveryWindow, AbortToken);
        var delivery = await _ReceiveUntilAsync(session, after.Id, RecoveryWindow);
        await session.Consumer.CommitAsync(delivery.SettlementValue, AbortToken);

        (await session.RemainsEmptyAsync(session.NoRedeliveryWindow, AbortToken))
            .Should()
            .BeTrue("a dropped connection must not replay a settled delivery");
    }

    /// <summary>
    /// Publishes keep failing for several reconnect attempts. They may fail, but they must not take the consumer
    /// down with them: once the network is back, the same consumer receives without being rebuilt.
    /// </summary>
    [Guarantee("MSG-CONN-02")]
    public virtual async Task should_keep_consuming_after_publishes_during_an_outage()
    {
        RequireSupport(TransportConformanceScenario.PublishDuringOutage);

        await using var faulted = await CreateNetworkFaultSessionAsync(AbortToken);
        var session = faulted.Session;
        var clock = session.TimeProvider;
        await session.StartAsync(cancellationToken: AbortToken);
        await _ProveLiveAsync(session, "before-outage");

        await faulted.Link.CutAsync(AbortToken);

        // Spans several client reconnect attempts, so a publish lands while the client is mid-recovery. The outage
        // stays short of the point where a client gives up and surfaces the fault for the host to rebuild it.
        var outageStarted = clock.GetTimestamp();
        for (var attempt = 0; clock.GetElapsedTime(outageStarted) < OutageDuration; attempt++)
        {
            var during = _CreateProbe(session.Destination, $"during-outage-{attempt}");
            try
            {
                await session.PublishAsync(during, AbortToken).WaitAsync(TimeSpan.FromSeconds(5), clock, AbortToken);
            }
#pragma warning disable CA1031 // A publish during an outage may fail or throw; only its effect on the consumer is asserted.
            catch (Exception ex) when (ex is not OperationCanceledException || !AbortToken.IsCancellationRequested)
#pragma warning restore CA1031
            {
                // Expected: the network is down.
            }

            await clock.Delay(TimeSpan.FromSeconds(1), AbortToken);
        }

        await faulted.Link.RestoreAsync(AbortToken);

        var after = _CreateProbe(session.Destination, "after-outage");
        await session.PublishUntilSucceededAsync(after, RecoveryWindow, AbortToken);
        var delivery = await _ReceiveUntilAsync(session, after.Id, RecoveryWindow);
        await session.Consumer.CommitAsync(delivery.SettlementValue, AbortToken);
    }

    /// <summary>
    /// Proves the session's client reaches the broker only over its link. While the link is cut, a message published
    /// past the link is not delivered and a publish through the session does not succeed; once the link is back, the
    /// message is delivered once. A client that learns broker addresses from the broker can route around a proxy, and
    /// then every network-fault scenario passes without cutting anything, so such a leaf runs this as its own test.
    /// </summary>
    /// <param name="publishPastTheLink">Publishes to the same broker without going through the link.</param>
    protected async Task AssertTrafficStopsWhileTheLinkIsCutAsync(
        Func<TransportMessage, CancellationToken, Task<OperateResult>> publishPastTheLink
    )
    {
        await using var faulted = await CreateNetworkFaultSessionAsync(AbortToken);
        var session = faulted.Session;
        await session.StartAsync(cancellationToken: AbortToken);
        await _ProveLiveAsync(session, "before-cut");

        await faulted.Link.CutAsync(AbortToken);

        var held = _CreateProbe(session.Destination, "past-the-link");
        (await publishPastTheLink(held, AbortToken))
            .Succeeded.Should()
            .BeTrue("the broker stays reachable past the link");
        (await session.RemainsEmptyAsync(_CutObservationWindow, AbortToken))
            .Should()
            .BeTrue(
                "a consumer behind a cut link must not receive; a delivery means its client reaches the broker another way"
            );

        var blocked = _CreateProbe(session.Destination, "through-the-cut-link");
        (await _TryPublishAsync(session, blocked))
            .Should()
            .BeFalse(
                "a publish over a cut link must not succeed; a success means the producer reaches the broker another way"
            );

        await faulted.Link.RestoreAsync(AbortToken);

        // The blocked publish may still reach the broker once the link is back, so it is settled like any other.
        var delivery = await _ReceiveUntilAsync(session, held.Id, RecoveryWindow);
        await session.Consumer.CommitAsync(delivery.SettlementValue, AbortToken);
        await _AssertNotRedeliveredAsync(session, held.Id);
    }

    private async Task<bool> _TryPublishAsync(TransportConsumerConformanceSession session, TransportMessage message)
    {
        try
        {
            var result = await session
                .PublishAsync(message, AbortToken)
                .WaitAsync(TimeSpan.FromSeconds(5), session.TimeProvider, AbortToken);

            return result.Succeeded;
        }
#pragma warning disable CA1031 // A publish over a cut link may fail, throw, or never finish; each one means it did not succeed.
        catch (Exception ex) when (ex is not OperationCanceledException || !AbortToken.IsCancellationRequested)
#pragma warning restore CA1031
        {
            return false;
        }
    }

    private async Task _AssertNotRedeliveredAsync(TransportConsumerConformanceSession session, string messageId)
    {
        var clock = session.TimeProvider;
        var started = clock.GetTimestamp();

        while (session.NoRedeliveryWindow - clock.GetElapsedTime(started) is var remaining && remaining > TimeSpan.Zero)
        {
            TransportConformanceDelivery delivery;
            try
            {
                delivery = await session.ReceiveAsync(remaining, AbortToken);
            }
            catch (TimeoutException)
            {
                return;
            }

            delivery
                .Message.Id.Should()
                .NotBe(messageId, "a cut link must not replay a delivery settled after it came back");
            await session.Consumer.CommitAsync(delivery.SettlementValue, AbortToken);
        }
    }

    private async Task _ProveLiveAsync(TransportConsumerConformanceSession session, string label)
    {
        var probe = _CreateProbe(session.Destination, label);
        (await session.PublishAsync(probe, AbortToken)).Succeeded.Should().BeTrue();
        var delivery = await session.ReceiveAsync(TimeSpan.FromSeconds(10), AbortToken);
        delivery.Message.Id.Should().Be(probe.Id);
        await session.Consumer.CommitAsync(delivery.SettlementValue, AbortToken);
    }

    /// <summary>Receives until the wanted message arrives, settling anything else that a failed publish left behind.</summary>
    private async Task<TransportConformanceDelivery> _ReceiveUntilAsync(
        TransportConsumerConformanceSession session,
        string messageId,
        TimeSpan window
    )
    {
        var clock = session.TimeProvider;
        var started = clock.GetTimestamp();

        while (true)
        {
            var remaining = window - clock.GetElapsedTime(started);
            var delivery = await session.ReceiveAsync(
                remaining > TimeSpan.Zero ? remaining : TimeSpan.FromMilliseconds(1),
                AbortToken
            );
            if (string.Equals(delivery.Message.Id, messageId, StringComparison.Ordinal))
            {
                return delivery;
            }

            await session.Consumer.CommitAsync(delivery.SettlementValue, AbortToken);
        }
    }

    private static TransportMessage _CreateProbe(string destination, string label)
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = Guid.NewGuid().ToString("N"),
            [Headers.MessageName] = destination,
            [Headers.Intent] = nameof(MessageLane.Queue),
            ["x-headless-conformance"] = label,
        };

        return new TransportMessage(headers, System.Text.Encoding.UTF8.GetBytes(label));
    }
}
