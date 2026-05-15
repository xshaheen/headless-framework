// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
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
    public ValueTask Consume(ConsumeContext<AlphaEvent> context, CancellationToken ct) => ValueTask.CompletedTask;
}

public sealed record BetaEvent(string Id);

public sealed class BetaConsumer : IConsume<BetaEvent>
{
    public ValueTask Consume(ConsumeContext<BetaEvent> context, CancellationToken ct) => ValueTask.CompletedTask;
}

// ─── Fixture ─────────────────────────────────────────────────────────────────

public sealed class SharedHarnessFixture : IAsyncLifetime
{
    public MessagingTestHarness Harness { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Harness = await MessagingTestHarness.CreateAsync(services =>
        {
            services.AddHeadlessMessaging(setup =>
            {
                setup.UseInMemoryMessageQueue();
                setup.UseInMemoryStorage();
                setup.Subscribe<AlphaConsumer>("alpha-topic");
                setup.Subscribe<BetaConsumer>("beta-topic");
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
/// Proves that <see cref="MessagingTestHarness.Clear"/> resets all in-memory
/// messaging state so a shared host can be reused across tests without leakage.
/// Uses <see cref="IClassFixture{T}"/> so a single harness instance is shared
/// across all test methods — each test calls <c>Clear()</c> to prove isolation.
/// </summary>
public sealed class SharedHostIsolationTests(SharedHarnessFixture fixture)
    : TestBase,
        IClassFixture<SharedHarnessFixture>
{
    private readonly MessagingTestHarness _harness = fixture.Harness;

    [Fact]
    public async Task should_isolate_observations_after_clear()
    {
        _harness.Clear();

        // First round: publish Alpha
        await _harness.Publisher.PublishAsync(new AlphaEvent("A1"), AbortToken);
        await _harness.WaitForConsumed<AlphaEvent>(TimeSpan.FromSeconds(5), AbortToken);

        _harness.Published.Should().ContainSingle();
        _harness.Consumed.Should().ContainSingle();

        // Clear all state
        _harness.Clear();

        // Second round: publish Beta
        await _harness.Publisher.PublishAsync(new BetaEvent("B1"), AbortToken);
        await _harness.WaitForConsumed<BetaEvent>(TimeSpan.FromSeconds(5), AbortToken);

        // Should see only Beta, not Alpha
        _harness.Published.Should().ContainSingle();
        _harness.Consumed.Should().ContainSingle();
        _harness.Consumed.Single().Message.Should().BeOfType<BetaEvent>().Which.Id.Should().Be("B1");
    }

    [Fact]
    public async Task should_reset_storage_layer_after_clear()
    {
        _harness.Clear();

        // Publish and consume to populate storage
        await _harness.Publisher.PublishAsync(new AlphaEvent("S1"), AbortToken);
        await _harness.WaitForConsumed<AlphaEvent>(TimeSpan.FromSeconds(5), AbortToken);

        // Verify storage has received-message data before clear
        // (test harness uses IDirectPublisher which bypasses outbox storage,
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

        // Clear all state
        _harness.Clear();

        // Verify storage is empty (re-create monitoring API to read fresh state)
        var monitoringAfter = _harness.ServiceProvider.GetRequiredService<IDataStorage>().GetMonitoringApi();
        var pageAfter = await monitoringAfter.GetMessagesAsync(query, AbortToken);
        pageAfter.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task should_support_full_publish_consume_cycle_after_clear()
    {
        _harness.Clear();

        // First cycle
        await _harness.Publisher.PublishAsync(new AlphaEvent("A3"), AbortToken);
        await _harness.WaitForConsumed<AlphaEvent>(TimeSpan.FromSeconds(5), AbortToken);

        _harness.Clear();

        // Second cycle — should work identically
        await _harness.Publisher.PublishAsync(new AlphaEvent("A4"), AbortToken);
        var recorded = await _harness.WaitForConsumed<AlphaEvent>(TimeSpan.FromSeconds(5), AbortToken);

        recorded.Message.Should().BeOfType<AlphaEvent>().Which.Id.Should().Be("A4");
        _harness.Consumed.Should().ContainSingle();
    }

    [Fact]
    public void should_not_throw_when_clear_called_on_empty_state()
    {
        _harness.Clear();

        var act = () => _harness.Clear();

        act.Should().NotThrow();
        _harness.Published.Should().BeEmpty();
        _harness.Consumed.Should().BeEmpty();
        _harness.Faulted.Should().BeEmpty();
    }
}
