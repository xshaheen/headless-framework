// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Tests.Capabilities;
using Tests.Guarantees;
using Tests.Toxiproxy;

namespace Tests;

/// <summary>
/// A conformance session whose client reaches the broker through a Toxiproxy route, so a test can cut the network
/// while the broker keeps running.
/// </summary>
[PublicAPI]
public sealed class ProxiedTransportSession(
    TransportConsumerConformanceSession session,
    ToxiproxyProxy proxy,
    Func<ValueTask> disposeResources
) : IAsyncDisposable
{
    public TransportConsumerConformanceSession Session { get; } = session;

    public ToxiproxyProxy Proxy { get; } = proxy;

    public async ValueTask DisposeAsync()
    {
        try
        {
            await Proxy.EnableAsync().ConfigureAwait(false);
        }
#pragma warning disable CA1031, ERP022 // Cleanup must reach the session and container disposal even when the proxy is gone.
        catch (Exception)
        {
            // The proxy may already be torn down with its container.
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
[PublicAPI]
public abstract class NetworkFaultTestsBase : TransportConsumerConformanceSessionTestsBase
{
    /// <summary>How long a client may take to reconnect and deliver again once the network is back.</summary>
    protected virtual TimeSpan RecoveryWindow => TimeSpan.FromSeconds(60);

    /// <summary>How long the network stays down while publishes are attempted.</summary>
    protected virtual TimeSpan OutageDuration => TimeSpan.FromSeconds(15);

    /// <summary>
    /// How long the client reconnects before the first publish. A publish during recovery can itself rebuild the
    /// connection, so the drop scenario stays quiet to tell a client that cannot reconnect from one a publish broke.
    /// Reconnect back-off runs on real time, so this is a wait on the client, not on the test.
    /// </summary>
    protected virtual TimeSpan QuietReconnectPeriod => TimeSpan.FromSeconds(20);

    /// <summary>
    /// Starts a Toxiproxy server, routes it to a broker, and opens a session whose producer and consumer connect
    /// through that route. The returned value owns the Toxiproxy server and any broker it started.
    /// </summary>
    protected abstract ValueTask<ProxiedTransportSession> CreateProxiedSessionAsync(
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

        await using var proxied = await CreateProxiedSessionAsync(AbortToken);
        var session = proxied.Session;
        await session.StartAsync(cancellationToken: AbortToken);
        await _ProveLiveAsync(session, "before-drop");

        await proxied.Proxy.DisableAsync(AbortToken);
        await proxied.Proxy.EnableAsync(AbortToken);
        await Task.Delay(QuietReconnectPeriod, AbortToken);

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

        await using var proxied = await CreateProxiedSessionAsync(AbortToken);
        var session = proxied.Session;
        await session.StartAsync(cancellationToken: AbortToken);
        await _ProveLiveAsync(session, "before-outage");

        await proxied.Proxy.DisableAsync(AbortToken);

        // Spans several client reconnect attempts, so a publish lands while the client is mid-recovery. The outage
        // stays short of the point where a client gives up and surfaces the fault for the host to rebuild it.
        var outageStarted = TimeProvider.System.GetTimestamp();
        for (var attempt = 0; TimeProvider.System.GetElapsedTime(outageStarted) < OutageDuration; attempt++)
        {
            var during = _CreateProbe(session.Destination, $"during-outage-{attempt}");
            try
            {
                await session.PublishAsync(during, AbortToken).WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
            }
#pragma warning disable CA1031 // A publish during an outage may fail or throw; only its effect on the consumer is asserted.
            catch (Exception ex) when (ex is not OperationCanceledException || !AbortToken.IsCancellationRequested)
#pragma warning restore CA1031
            {
                // Expected: the network is down.
            }

            await Task.Delay(TimeSpan.FromSeconds(1), AbortToken);
        }

        await proxied.Proxy.EnableAsync(AbortToken);

        var after = _CreateProbe(session.Destination, "after-outage");
        await session.PublishUntilSucceededAsync(after, RecoveryWindow, AbortToken);
        var delivery = await _ReceiveUntilAsync(session, after.Id, RecoveryWindow);
        await session.Consumer.CommitAsync(delivery.SettlementValue, AbortToken);
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
        var deadline = TimeProvider.System.GetTimestamp();

        while (true)
        {
            var remaining = window - TimeProvider.System.GetElapsedTime(deadline);
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
