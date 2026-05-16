// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Retry;
using Headless.Messaging.Testing;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

// ─── Message types ────────────────────────────────────────────────────────────

public sealed record OrderCreatedEvent(string OrderId, decimal Amount);

public sealed class OrderCreatedConsumer : IConsume<OrderCreatedEvent>
{
    public ValueTask Consume(ConsumeContext<OrderCreatedEvent> context, CancellationToken ct) =>
        ValueTask.CompletedTask;
}

public sealed class FailingConsumer : IConsume<OrderCreatedEvent>
{
    public ValueTask Consume(ConsumeContext<OrderCreatedEvent> context, CancellationToken ct) =>
        throw new InvalidOperationException("Test failure");
}

public interface INotificationService
{
    void Notify(string orderId);
}

public sealed class NotifyingConsumer(INotificationService notifier) : IConsume<OrderCreatedEvent>
{
    public ValueTask Consume(ConsumeContext<OrderCreatedEvent> context, CancellationToken ct)
    {
        notifier.Notify(context.Message.OrderId);
        return ValueTask.CompletedTask;
    }
}

// ─── Tests ────────────────────────────────────────────────────────────────────

public sealed class EndToEndTests : TestBase
{
    private static Task<MessagingTestHarness> _CreateHarnessAsync(Action<MessagingSetupBuilder>? configure = null)
    {
        return MessagingTestHarness.CreateAsync(services =>
        {
            services.AddHeadlessMessaging(setup =>
            {
                setup.UseInMemoryMessageQueue();
                setup.UseInMemoryStorage();
                configure?.Invoke(setup);
            });
        });
    }

    // ─── Test 1: publish → consume → assert ──────────────────────────────────

    [Fact]
    public async Task Publish_and_consume_flow_works_end_to_end()
    {
        // given
        await using var harness = await _CreateHarnessAsync(options =>
        {
            options.Subscribe<OrderCreatedConsumer>("order-created");
        });

        // when
        await harness.Publisher.PublishAsync(new OrderCreatedEvent("ORD-001", 99.99m), AbortToken);
        var recorded = await harness.WaitForConsumed<OrderCreatedEvent>(TimeSpan.FromSeconds(5), AbortToken);

        // then
        recorded.MessageId.Should().NotBeNullOrWhiteSpace();
        recorded.Topic.Should().NotBeNullOrWhiteSpace();
        recorded.MessageType.Should().Be(typeof(OrderCreatedEvent));
        recorded.Message.Should().BeOfType<OrderCreatedEvent>().Which.OrderId.Should().Be("ORD-001");

        harness.Published.Should().ContainSingle();
        harness.Consumed.Should().ContainSingle();
        harness.Faulted.Should().BeEmpty();
    }

    // ─── Test 2: faulted observation captures exception ───────────────────────

    [Fact]
    public async Task Faulted_observation_captures_exception()
    {
        // given
        await using var harness = await _CreateHarnessAsync(options =>
        {
            options.Subscribe<FailingConsumer>("order-created");
        });

        // when
        await harness.Publisher.PublishAsync(new OrderCreatedEvent("ORD-002", 50m), AbortToken);
        var faulted = await harness.WaitForFaulted<OrderCreatedEvent>(TimeSpan.FromSeconds(5), AbortToken);

        // then
        faulted.Exception.Should().NotBeNull();
        faulted.Exception.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Be("Test failure");

        harness.Faulted.Should().ContainSingle();
        harness.Consumed.Should().BeEmpty();
    }

    // ─── Test 3: WaitForConsumed with predicate filters correctly ─────────────

    [Fact]
    public async Task WaitForConsumed_with_predicate_filters_correctly()
    {
        // given — use TestConsumer so we get a singleton that handles the messages
        await using var harness = await MessagingTestHarness.CreateAsync(services =>
        {
            services.AddSingleton<TestConsumer<OrderCreatedEvent>>();
            services.AddHeadlessMessaging(options =>
            {
                options.UseInMemoryMessageQueue();
                options.UseInMemoryStorage();
                options.Subscribe<TestConsumer<OrderCreatedEvent>>("order-created");
            });
        });

        // Publish a non-target message first
        await harness.Publisher.PublishAsync(new OrderCreatedEvent("other", 1m), AbortToken);

        // Publish the target message
        await harness.Publisher.PublishAsync(new OrderCreatedEvent("target", 200m), AbortToken);

        // when — wait for specifically the "target" order
        var recorded = await harness.WaitForConsumed<OrderCreatedEvent>(
            msg => msg.OrderId == "target",
            TimeSpan.FromSeconds(5),
            AbortToken
        );

        // then
        recorded.Message.Should().BeOfType<OrderCreatedEvent>().Which.OrderId.Should().Be("target");
        recorded.MessageType.Should().Be(typeof(OrderCreatedEvent));
    }

    // ─── Test 4: timeout produces descriptive exception ───────────────────────

    [Fact]
    public async Task Timeout_produces_descriptive_exception()
    {
        // given — no consumer registered; message never arrives
        await using var harness = await _CreateHarnessAsync();

        // when
        var act = async () =>
            await harness.WaitForConsumed<OrderCreatedEvent>(TimeSpan.FromMilliseconds(100), AbortToken);

        // then
        var ex = await act.Should().ThrowAsync<MessageObservationTimeoutException>();
        ex.Which.ExpectedType.Should().Be(typeof(OrderCreatedEvent));
        ex.Which.ObservationType.Should().Be(MessageObservationType.Consumed);
        ex.Which.Message.Should().Contain(nameof(OrderCreatedEvent));
        ex.Which.Elapsed.Should().BeGreaterThan(TimeSpan.Zero);
    }

    // ─── Test 5: two harness instances do not cross-contaminate ──────────────

    [Fact]
    public async Task Two_harness_instances_do_not_cross_contaminate()
    {
        // given
        await using var harness1 = await _CreateHarnessAsync(options =>
        {
            options.Subscribe<OrderCreatedConsumer>("order-created");
        });

        await using var harness2 = await _CreateHarnessAsync(options =>
        {
            options.Subscribe<OrderCreatedConsumer>("order-created");
        });

        // when — publish only in harness1
        await harness1.Publisher.PublishAsync(new OrderCreatedEvent("H1-ORD", 10m), AbortToken);
        await harness1.WaitForConsumed<OrderCreatedEvent>(TimeSpan.FromSeconds(5), AbortToken);

        // then — harness2 should be untouched
        harness2.Published.Should().BeEmpty();
        harness2.Consumed.Should().BeEmpty();
        harness2.Faulted.Should().BeEmpty();
    }

    // ─── Test 6: consumer with injected dependency resolves correctly ─────────

    [Fact]
    public async Task Consumer_with_injected_dependency_resolves_correctly()
    {
        // given
        var notifier = Substitute.For<INotificationService>();

        await using var harness = await MessagingTestHarness.CreateAsync(services =>
        {
            services.AddSingleton(notifier);
            services.AddHeadlessMessaging(options =>
            {
                options.UseInMemoryMessageQueue();
                options.UseInMemoryStorage();
                options.Subscribe<NotifyingConsumer>("order-created");
            });
        });

        // when
        await harness.Publisher.PublishAsync(new OrderCreatedEvent("ORD-INJ", 75m), AbortToken);
        await harness.WaitForConsumed<OrderCreatedEvent>(TimeSpan.FromSeconds(5), AbortToken);

        // then — the mock was invoked with the correct order ID
        notifier.Received(1).Notify("ORD-INJ");
    }

    // ─── Test 7: AddMessagingTestHarness registers into existing container ───

    [Fact]
    public async Task AddMessagingTestHarness_registers_into_existing_container()
    {
        // given — simulate a host-owned ServiceCollection
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(options =>
        {
            options.UseInMemoryMessageQueue();
            options.UseInMemoryStorage();
            options.Subscribe<OrderCreatedConsumer>("order-created");
        });

        // Register the test harness via extension method (hosted mode)
        services.AddMessagingTestHarness();

        await using var sp = services.BuildServiceProvider();

        // Bootstrap manually (in a real host, the HostedService does this)
        var bootstrapper = sp.GetRequiredService<IBootstrapper>();
        await bootstrapper.BootstrapAsync(AbortToken);

        var harness = sp.GetRequiredService<MessagingTestHarness>();

        // when
        var publisher = sp.GetRequiredService<IMessagePublisher>();
        await publisher.PublishAsync(new OrderCreatedEvent("HOSTED-1", 42m), AbortToken);
        var recorded = await harness.WaitForConsumed<OrderCreatedEvent>(TimeSpan.FromSeconds(5), AbortToken);

        // then — harness observes messages through the same container
        recorded.Message.Should().BeOfType<OrderCreatedEvent>().Which.OrderId.Should().Be("HOSTED-1");
        harness.Published.Should().ContainSingle();
        harness.Consumed.Should().ContainSingle();
    }

    // ─── Test 8: WaitForExhausted observes user OnExhausted callback ──────────

    [Fact]
    public async Task WaitForExhausted_observes_user_callback_invocation()
    {
        // given — single-attempt budget so failure becomes terminal immediately
        var userCallbackFired = 0;

        await using var harness = await _CreateHarnessAsync(options =>
        {
            options.Subscribe<FailingConsumer>("order-created");

            options.Options.RetryPolicy.MaxInlineRetries = 0;
            options.Options.RetryPolicy.MaxPersistedRetries = 0;
            options.Options.RetryPolicy.BackoffStrategy = new FixedIntervalBackoffStrategy(TimeSpan.Zero);
            options.Options.RetryPolicy.OnExhausted = (_, _) =>
            {
                Interlocked.Increment(ref userCallbackFired);
                return Task.CompletedTask;
            };
        });

        // when
        await harness.Publisher.PublishAsync(new OrderCreatedEvent("ORD-EXH", 1m), AbortToken);
        var recorded = await harness.WaitForExhausted<OrderCreatedEvent>(TimeSpan.FromSeconds(5), AbortToken);

        // then — observation captures the exhausted message and its exception
        recorded.MessageType.Should().Be(typeof(OrderCreatedEvent));
        recorded.Message.Should().BeOfType<OrderCreatedEvent>().Which.OrderId.Should().Be("ORD-EXH");
        // The exception is the pipeline-wrapped failure; the original "Test failure" is the inner.
        recorded.Exception.Should().NotBeNull();
        recorded.Exception!.GetBaseException().Message.Should().Be("Test failure");

        // The user-supplied callback also ran (recording wraps, does not replace)
        await TestHelpers.WaitForAsync(() => userCallbackFired == 1, TimeSpan.FromSeconds(5));
        userCallbackFired.Should().Be(1);

        harness.Exhausted.Should().ContainSingle();
        harness.Faulted.Should().NotBeEmpty();
    }

    // ─── Test 9: WaitForExhausted survives a hanging user callback ───────────

    [Fact]
    public async Task WaitForExhausted_records_before_user_callback_runs()
    {
        // given — user callback hangs; observation must still be visible
        using var hangGate = new ManualResetEventSlim(initialState: false);

        await using var harness = await _CreateHarnessAsync(options =>
        {
            options.Subscribe<FailingConsumer>("order-created");

            options.Options.RetryPolicy.MaxInlineRetries = 0;
            options.Options.RetryPolicy.MaxPersistedRetries = 0;
            options.Options.RetryPolicy.BackoffStrategy = new FixedIntervalBackoffStrategy(TimeSpan.Zero);
            options.Options.RetryPolicy.OnExhaustedTimeout = TimeSpan.FromMilliseconds(200);
            options.Options.RetryPolicy.OnExhausted = async (_, ct) =>
            {
                // Park the callback until the test releases it (or the timeout fires).
                await Task.Run(() => hangGate.Wait(ct), ct);
            };
        });

        try
        {
            // when
            await harness.Publisher.PublishAsync(new OrderCreatedEvent("ORD-HANG", 1m), AbortToken);
            var recorded = await harness.WaitForExhausted<OrderCreatedEvent>(TimeSpan.FromSeconds(5), AbortToken);

            // then — the observation arrived even though the user callback is still parked
            recorded.Message.Should().BeOfType<OrderCreatedEvent>().Which.OrderId.Should().Be("ORD-HANG");
        }
        finally
        {
            hangGate.Set();
        }
    }
}

file static class TestHelpers
{
    public static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(20);
        }
    }
}
