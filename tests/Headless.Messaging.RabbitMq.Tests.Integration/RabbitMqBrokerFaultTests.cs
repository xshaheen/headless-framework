// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

[Collection<RabbitMqFixture>]
public sealed class RabbitMqBrokerFaultTests(RabbitMqFixture fixture) : BrokerFaultTestsBase
{
    protected override string ProviderName => "RabbitMQ";

    protected override ValueTask<TransportConsumerConformanceSession> CreateSessionAsync(
        CancellationToken cancellationToken
    )
    {
        return fixture.CreateConformanceSessionAsync(cancellationToken);
    }

    protected override async ValueTask<InterruptibleTransportSession> CreateInterruptibleSessionAsync(
        CancellationToken cancellationToken
    )
    {
        // A broker of its own at a stable address: a restart must not disturb the collection's broker, and the
        // session must find the restarted broker at the address it already holds.
#pragma warning disable CA2000 // Ownership moves to the returned session, or the catch disposes it.
        var broker = new FixedPortRabbitMqFixture();
#pragma warning restore CA2000

        try
        {
            await broker.StartAsync();
            var session = await RabbitMqFixture.CreateConformanceSessionAsync(
                broker.Host,
                broker.Port,
                cancellationToken
            );

            return new InterruptibleTransportSession(
                session,
                async token =>
                {
                    // A basic.ack is never confirmed, so a stop can overtake it and the broker requeues a delivery
                    // the test already settled. Wait until the broker has applied every ack before stopping.
                    await _WaitForNoUnacknowledgedAsync(broker, token);
                    await broker.Container.StopAsync(token);
                    await broker.Container.StartAsync(token);
                },
                () => ((IAsyncDisposable)broker).DisposeAsync()
            );
        }
        catch
        {
            await ((IAsyncDisposable)broker).DisposeAsync();
            throw;
        }
    }

    private static async Task _WaitForNoUnacknowledgedAsync(
        FixedPortRabbitMqFixture broker,
        CancellationToken cancellationToken
    )
    {
        var deadline = TimeProvider.System.GetTimestamp() + (long)(10 * TimeProvider.System.TimestampFrequency);
        var lastOutput = string.Empty;

        while (TimeProvider.System.GetTimestamp() < deadline)
        {
            // rabbitmqctl occasionally stalls without answering, so each call is bounded and the loop asks again.
            using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            bounded.CancelAfter(TimeSpan.FromSeconds(5));

            try
            {
                var result = await broker.Container.ExecAsync(
                    ["rabbitmqctl", "list_queues", "-q", "--no-table-headers", "messages_unacknowledged"],
                    bounded.Token
                );

                lastOutput = result.Stdout;

                if (
                    result.ExitCode == 0
                    && result
                        .Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .All(static count => string.Equals(count, "0", StringComparison.Ordinal))
                )
                {
                    return;
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // The bounded call timed out; ask again.
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        }

        throw new TimeoutException($"The broker still held unacknowledged messages before the restart: {lastOutput}");
    }

    [Fact]
    public override Task should_resume_delivery_once_after_consumer_pause()
    {
        return base.should_resume_delivery_once_after_consumer_pause();
    }

    [Fact]
    public override Task should_resume_delivery_once_after_broker_restart()
    {
        return base.should_resume_delivery_once_after_broker_restart();
    }
}
