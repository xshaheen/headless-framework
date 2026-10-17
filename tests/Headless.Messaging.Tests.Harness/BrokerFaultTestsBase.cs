// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Tests.Capabilities;
using Tests.Guarantees;

namespace Tests;

/// <summary>
/// A conformance session on a broker the test may restart, with the means to restart it at the address the session
/// already uses.
/// </summary>
[PublicAPI]
public sealed class InterruptibleTransportSession(
    TransportConsumerConformanceSession session,
    Func<CancellationToken, Task> restartBroker,
    Func<ValueTask> disposeBroker
) : IAsyncDisposable
{
    public TransportConsumerConformanceSession Session { get; } = session;

    public Task RestartBrokerAsync(CancellationToken cancellationToken) => restartBroker(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        try
        {
            await Session.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            await disposeBroker().ConfigureAwait(false);
        }
    }
}

/// <summary>Shared optional recovery scenarios for broker-backed transport leaves.</summary>
[PublicAPI]
public abstract class BrokerFaultTestsBase : TransportConsumerConformanceSessionTestsBase
{
    /// <summary>
    /// Creates a session on a broker of its own, so a restart cannot disturb other tests. The default declines, which
    /// leaves <see cref="should_resume_delivery_once_after_broker_restart"/> unrunnable for a provider that has not
    /// supplied one.
    /// </summary>
    protected virtual ValueTask<InterruptibleTransportSession> CreateInterruptibleSessionAsync(
        CancellationToken cancellationToken
    )
    {
        throw new NotSupportedException($"{GetType().Name} does not supply an interruptible broker.");
    }

    public virtual async Task should_resume_delivery_once_after_consumer_pause()
    {
        RequireSupport(TransportConformanceScenario.ConsumerPauseRecovery);

        await using var session = await CreateSessionAsync(AbortToken);
        await session.StartAsync(cancellationToken: AbortToken);
        await session.Consumer.PauseAsync(AbortToken);

        var message = CreateFaultProbe(session.Destination);
        var result = await session.PublishAsync(message, AbortToken);
        result.Succeeded.Should().BeTrue();

        var stayedPaused = await session.RemainsEmptyAsync(TimeSpan.FromMilliseconds(750), AbortToken);
        stayedPaused.Should().BeTrue("a paused consumer must not dispatch new broker deliveries");

        await session.Consumer.ResumeAsync(AbortToken);
        var delivery = await session.ReceiveAsync(TimeSpan.FromSeconds(10), AbortToken);
        await session.Consumer.CommitAsync(delivery.SettlementValue, AbortToken);

        var receivedOnce = await session.RemainsEmptyAsync(session.NoRedeliveryWindow, AbortToken);
        receivedOnce.Should().BeTrue("resume must not register a duplicate callback or redeliver the committed probe");
    }

    /// <summary>
    /// A consumer and a producer that outlive a broker restart must both keep working, without the host rebuilding
    /// either, and the first delivery must not be replayed.
    /// </summary>
    [Guarantee("MSG-CONN-01")]
    public virtual async Task should_resume_delivery_once_after_broker_restart()
    {
        RequireSupport(TransportConformanceScenario.BrokerInterruptionRecovery);

        await using var interruptible = await CreateInterruptibleSessionAsync(AbortToken);
        var session = interruptible.Session;
        await session.StartAsync(cancellationToken: AbortToken);

        var beforeRestart = CreateFaultProbe(session.Destination);
        (await session.PublishAsync(beforeRestart, AbortToken)).Succeeded.Should().BeTrue();
        var beforeDelivery = await session.ReceiveAsync(TimeSpan.FromSeconds(10), AbortToken);
        await session.Consumer.CommitAsync(beforeDelivery.SettlementValue, AbortToken);

        await interruptible.RestartBrokerAsync(AbortToken);

        var afterRestart = CreateFaultProbe(session.Destination);
        await session.PublishUntilSucceededAsync(afterRestart, TimeSpan.FromSeconds(60), AbortToken);

        var afterDelivery = await session.ReceiveAsync(TimeSpan.FromSeconds(60), AbortToken);
        afterDelivery.Message.Id.Should().Be(afterRestart.Id);
        await session.Consumer.CommitAsync(afterDelivery.SettlementValue, AbortToken);

        (await session.RemainsEmptyAsync(session.NoRedeliveryWindow, AbortToken))
            .Should()
            .BeTrue("a restart must not replay the settled pre-restart delivery");
    }

    protected virtual TransportMessage CreateFaultProbe(string destination)
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = Guid.NewGuid().ToString("N"),
            [Headers.MessageName] = destination,
            ["x-headless-conformance"] = "consumer-pause-recovery",
        };

        return new TransportMessage(headers, "post-pause-probe"u8.ToArray());
    }
}
