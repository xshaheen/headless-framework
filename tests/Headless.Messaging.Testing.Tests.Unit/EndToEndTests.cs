// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
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
    private static Task<MessagingTestHarness> _CreateHarnessAsync(Action<MessagingOptions>? configure = null)
    {
        return MessagingTestHarness.CreateAsync(services =>
        {
            services.AddHeadlessMessaging(options =>
            {
                options.UseInMemoryMessageQueue();
                options.UseInMemoryStorage();
                configure?.Invoke(options);
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
}
