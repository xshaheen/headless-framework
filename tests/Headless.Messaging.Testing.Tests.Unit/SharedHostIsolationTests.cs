// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Messaging.Testing;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

// ─── Message types ────────────────────────────────────────────────────────────

public sealed record AlphaEvent(string Id);

public sealed class AlphaConsumer : IConsume<AlphaEvent>
{
    public ValueTask ConsumeAsync(ConsumeContext<AlphaEvent> context, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}

public sealed record BetaEvent(string Id);

public sealed class BetaConsumer : IConsume<BetaEvent>
{
    public ValueTask ConsumeAsync(ConsumeContext<BetaEvent> context, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}

public sealed record GammaEvent(string Id);

/// <summary>Holds a consumption open until the test releases it, so a test can observe a reset racing in-flight work.</summary>
public sealed class GatedConsumer : IConsume<GammaEvent>
{
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Started => _started.Task;

    public void Release()
    {
        _gate.TrySetResult();
    }

    public async ValueTask ConsumeAsync(ConsumeContext<GammaEvent> context, CancellationToken cancellationToken)
    {
        _started.TrySetResult();
        await _gate.Task.WaitAsync(cancellationToken);
    }
}

public sealed record DeltaEvent(string Id);

/// <summary>
/// Same shape as <see cref="GatedConsumer"/> but a distinct type: the fixture registers each gated consumer as a
/// singleton, and a one-shot gate released by one test would let a later test's message flow straight through.
/// </summary>
public sealed class DeltaGatedConsumer : IConsume<DeltaEvent>
{
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Started => _started.Task;

    public void Release()
    {
        _gate.TrySetResult();
    }

    public async ValueTask ConsumeAsync(ConsumeContext<DeltaEvent> context, CancellationToken cancellationToken)
    {
        _started.TrySetResult();
        await _gate.Task.WaitAsync(cancellationToken);
    }
}

// ─── Fixture ─────────────────────────────────────────────────────────────────

public sealed class SharedHarnessFixture : IAsyncLifetime
{
    public MessagingTestHarness Harness { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Harness = await MessagingTestHarness.CreateAsync(services =>
        {
            services.AddSingleton<GatedConsumer>();
            services.AddSingleton<DeltaGatedConsumer>();
            services.AddHeadlessMessaging(setup =>
            {
                setup.UseInMemory();
                setup.UseInMemoryStorage();
                setup.Options.RequiredInboxCapability = MessagingInboxCapabilityTier.ProcessLocal;
                setup.Bus.ForMessage<AlphaEvent>(message =>
                    message
                        .Contract("alpha-messageName")
                        .Consumer<AlphaConsumer>(consumer => consumer.ConsumerIdentity("tests.messaging-testing.alpha"))
                );
                setup.Bus.ForMessage<BetaEvent>(message =>
                    message
                        .Contract("beta-messageName")
                        .Consumer<BetaConsumer>(consumer => consumer.ConsumerIdentity("tests.messaging-testing.beta"))
                );
                setup.Bus.ForMessage<GammaEvent>(message =>
                    message
                        .Contract("gamma-messageName")
                        .Consumer<GatedConsumer>(consumer => consumer.ConsumerIdentity("tests.messaging-testing.gamma"))
                );
                setup.Bus.ForMessage<DeltaEvent>(message =>
                    message
                        .Contract("delta-messageName")
                        .Consumer<DeltaGatedConsumer>(consumer =>
                            consumer.ConsumerIdentity("tests.messaging-testing.delta")
                        )
                );
            });
        });
    }

    public async ValueTask DisposeAsync()
    {
        await Harness.DisposeAsync();
    }
}

// ─── Tests ────────────────────────────────────────────────────────────────────

/// <summary>
/// Proves that <see cref="MessagingTestHarness.ResetAsync"/> waits for in-flight store-first work and then
/// resets all in-memory messaging state, so a shared host can be reused across tests without leakage.
/// Uses <see cref="IClassFixture{T}"/> so a single harness instance is shared across all test methods —
/// each test calls <c>ResetAsync()</c> to prove isolation.
/// </summary>
public sealed class SharedHostIsolationTests(SharedHarnessFixture fixture)
    : TestBase,
        IClassFixture<SharedHarnessFixture>
{
    private readonly MessagingTestHarness _harness = fixture.Harness;

    [Fact]
    public async Task should_isolate_observations_after_reset()
    {
        await _harness.ResetAsync(cancellationToken: AbortToken);

        // First round: publish Alpha
        await _harness.Publisher.PublishAsync(new AlphaEvent("A1"), cancellationToken: AbortToken);
        await _harness.WaitForConsumed<AlphaEvent>(TimeSpan.FromSeconds(5), AbortToken);

        _harness.Published.Should().ContainSingle();
        _harness.Consumed.Should().ContainSingle();

        // Reset all state
        await _harness.ResetAsync(cancellationToken: AbortToken);

        // Second round: publish Beta
        await _harness.Publisher.PublishAsync(new BetaEvent("B1"), cancellationToken: AbortToken);
        await _harness.WaitForConsumed<BetaEvent>(TimeSpan.FromSeconds(5), AbortToken);

        // Should see only Beta, not Alpha
        _harness.Published.Should().ContainSingle();
        _harness.Consumed.Should().ContainSingle();
        _harness.Consumed.Single().Message.Should().BeOfType<BetaEvent>().Which.Id.Should().Be("B1");
    }

    [Fact]
    public async Task should_not_leak_burst_publishes_into_next_round_after_reset()
    {
        await _harness.ResetAsync(cancellationToken: AbortToken);

        // Store-first publishing returns once the row is durable; the transport send, the Published
        // observation, and consumption all run on background dispatcher threads afterwards.
        for (var i = 0; i < 20; i++)
        {
            await _harness.Publisher.PublishAsync(new AlphaEvent($"A{i}"), cancellationToken: AbortToken);
        }

        await _harness.ResetAsync(cancellationToken: AbortToken);

        await _harness.Publisher.PublishAsync(new BetaEvent("B1"), cancellationToken: AbortToken);
        await _harness.WaitForConsumed<BetaEvent>(TimeSpan.FromSeconds(5), AbortToken);

        _harness.Published.Should().ContainSingle().Which.Message.Should().BeOfType<BetaEvent>();
        _harness.Consumed.Should().ContainSingle().Which.Message.Should().BeOfType<BetaEvent>();
    }

    [Fact]
    public async Task should_wait_for_in_flight_consumption_before_reset()
    {
        await _harness.ResetAsync(cancellationToken: AbortToken);
        var consumer = _harness.GetRequiredService<GatedConsumer>();

        await _harness.Publisher.PublishAsync(new GammaEvent("G1"), cancellationToken: AbortToken);
        await consumer.Started.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // The consumer is mid-execution: its received row is Scheduled and its Consumed observation is still to
        // come, so the reset must block until the gate opens instead of clearing underneath it.
        var reset = _harness.ResetAsync(cancellationToken: AbortToken);
        reset.IsCompleted.Should().BeFalse();

        consumer.Release();
        await reset;

        _harness.Consumed.Should().BeEmpty();
        _harness.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task should_timeout_naming_the_stuck_row_when_in_flight_work_never_settles()
    {
        await _harness.ResetAsync(cancellationToken: AbortToken);
        var consumer = _harness.GetRequiredService<DeltaGatedConsumer>();

        await _harness.Publisher.PublishAsync(new DeltaEvent("D1"), cancellationToken: AbortToken);
        await consumer.Started.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // The consumer holds its received row Scheduled for as long as the gate stays closed, so the settle
        // wait must run out of budget instead of observing an idle store.
        try
        {
            var act = () => _harness.ResetAsync(TimeSpan.FromSeconds(1), AbortToken);
            var ex = await act.Should().ThrowAsync<TimeoutException>();

            ex.Which.Message.Should().Contain("never settled").And.Contain("delta-messageName");
        }
        finally
        {
            // Release the stall so the shared harness settles before the next test's reset.
            consumer.Release();
        }
    }

    [Fact]
    public async Task should_not_wait_for_clock_parked_delayed_publish_before_reset()
    {
        await _harness.ResetAsync(cancellationToken: AbortToken);

        // A delay under one minute is stored as Queued and parked in the dispatcher's scheduler queue until it is
        // due on the host clock; no thread works on it, so the reset must not treat it as in flight.
        await _harness.Publisher.PublishAsync(
            new AlphaEvent("D1"),
            new PublishOptions { Delay = TimeSpan.FromSeconds(10) },
            AbortToken
        );

        var reset = _harness.ResetAsync(cancellationToken: AbortToken);
        await reset.WaitAsync(TimeSpan.FromSeconds(2), AbortToken);

        var monitoring = _harness.ServiceProvider.GetRequiredService<IDataStorage>().GetMonitoringApi();
        var query = new MessageQuery
        {
            MessageType = MessageType.Publish,
            CurrentPage = 0,
            PageSize = 10,
        };
        var page = await monitoring.GetMessagesAsync(query, AbortToken);
        page.Items.Should().BeEmpty();
        _harness.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task should_reset_storage_layer_after_reset()
    {
        await _harness.ResetAsync(cancellationToken: AbortToken);

        // Publish and consume to populate storage
        await _harness.Publisher.PublishAsync(new AlphaEvent("S1"), cancellationToken: AbortToken);
        await _harness.WaitForConsumed<AlphaEvent>(TimeSpan.FromSeconds(5), AbortToken);

        // Verify storage has received-message data before clear
        // (test harness uses IBus which bypasses outbox storage,
        //  but consumer pipeline stores in ReceivedMessages)
        var monitoring = _harness.ServiceProvider.GetRequiredService<IDataStorage>().GetMonitoringApi();
        var query = new MessageQuery
        {
            MessageType = MessageType.Subscribe,
            CurrentPage = 0,
            PageSize = 10,
        };
        var pageBefore = await monitoring.GetMessagesAsync(query, AbortToken);
        pageBefore.Items.Should().NotBeEmpty();

        // Reset all state
        await _harness.ResetAsync(cancellationToken: AbortToken);

        // Verify storage is empty (re-create monitoring API to read fresh state)
        var monitoringAfter = _harness.ServiceProvider.GetRequiredService<IDataStorage>().GetMonitoringApi();
        var pageAfter = await monitoringAfter.GetMessagesAsync(query, AbortToken);
        pageAfter.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task should_support_full_publish_consume_cycle_after_reset()
    {
        await _harness.ResetAsync(cancellationToken: AbortToken);

        // First cycle
        await _harness.Publisher.PublishAsync(new AlphaEvent("A3"), cancellationToken: AbortToken);
        await _harness.WaitForConsumed<AlphaEvent>(TimeSpan.FromSeconds(5), AbortToken);

        await _harness.ResetAsync(cancellationToken: AbortToken);

        // Second cycle — should work identically
        await _harness.Publisher.PublishAsync(new AlphaEvent("A4"), cancellationToken: AbortToken);
        var recorded = await _harness.WaitForConsumed<AlphaEvent>(TimeSpan.FromSeconds(5), AbortToken);

        recorded.Message.Should().BeOfType<AlphaEvent>().Which.Id.Should().Be("A4");
        _harness.Consumed.Should().ContainSingle();
    }

    [Fact]
    public async Task should_not_throw_when_reset_called_on_empty_state()
    {
        await _harness.ResetAsync(cancellationToken: AbortToken);

        var act = () => _harness.ResetAsync(cancellationToken: AbortToken);

        await act.Should().NotThrowAsync();
        _harness.Published.Should().BeEmpty();
        _harness.Consumed.Should().BeEmpty();
        _harness.Faulted.Should().BeEmpty();
    }
}
