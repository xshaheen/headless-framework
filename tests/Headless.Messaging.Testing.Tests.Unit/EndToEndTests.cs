// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Reflection;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Testing;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Polly.Retry;

namespace Tests;

// ─── Message types ────────────────────────────────────────────────────────────

public sealed record OrderCreatedEvent(string OrderId, decimal Amount);

public sealed class OrderCreatedConsumer : IConsume<OrderCreatedEvent>
{
    public ValueTask ConsumeAsync(ConsumeContext<OrderCreatedEvent> context, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}

public sealed class FailingConsumer : IConsume<OrderCreatedEvent>
{
    // Transient exception so the default RetryExceptionClassifier does NOT short-circuit to Stop.
    // The classifier (RetryExceptionClassifier.IsPermanent) now unwraps SubscriberExecutionFailedException
    // and treats InvalidOperationException as permanent — using TimeoutException keeps the failure
    // retryable so the exhaustion budget governs the terminal transition (and OnExhausted fires).
    public ValueTask ConsumeAsync(ConsumeContext<OrderCreatedEvent> context, CancellationToken cancellationToken)
    {
        throw new TimeoutException("Test failure");
    }
}

public interface INotificationService
{
    void Notify(string orderId);
}

public sealed class NotifyingConsumer(INotificationService notifier) : IConsume<OrderCreatedEvent>
{
    public ValueTask ConsumeAsync(ConsumeContext<OrderCreatedEvent> context, CancellationToken cancellationToken)
    {
        notifier.Notify(context.Message.OrderId);
        return ValueTask.CompletedTask;
    }
}

public sealed class IntentRecorder
{
    private readonly ConcurrentQueue<IntentType> _intents = [];

    public IReadOnlyCollection<IntentType> Intents => _intents.ToArray();

    public void Record(IntentType intentType)
    {
        _intents.Enqueue(intentType);
    }
}

public sealed class BusIntentConsumer(IntentRecorder recorder) : IConsume<OrderCreatedEvent>
{
    public ValueTask ConsumeAsync(ConsumeContext<OrderCreatedEvent> context, CancellationToken cancellationToken)
    {
        recorder.Record(context.IntentType);
        return ValueTask.CompletedTask;
    }
}

public sealed class QueueIntentConsumer(IntentRecorder recorder) : IConsume<OrderCreatedEvent>
{
    public ValueTask ConsumeAsync(ConsumeContext<OrderCreatedEvent> context, CancellationToken cancellationToken)
    {
        recorder.Record(context.IntentType);
        return ValueTask.CompletedTask;
    }
}

// ─── Tests ────────────────────────────────────────────────────────────────────

public sealed class EndToEndTests : TestBase
{
    private static Task<MessagingTestHarness> _CreateHarnessAsync(Action<MessagingSetupBuilder>? configure = null)
    {
        return _CreateHarnessAsync((_, setup) => configure?.Invoke(setup));
    }

    private static Task<MessagingTestHarness> _CreateHarnessAsync(
        Action<IServiceCollection, MessagingSetupBuilder> configure
    )
    {
        return MessagingTestHarness.CreateAsync(services =>
        {
            services.AddHeadlessMessaging(setup =>
            {
                setup.UseInMemory();
                setup.UseInMemoryStorage();
                configure(services, setup);
            });
        });
    }

    // ─── Test 1: publish → consume → assert ──────────────────────────────────

    [Fact]
    public async Task publish_and_consume_flow_works_end_to_end()
    {
        // given
        await using var harness = await _CreateHarnessAsync(
            (_, setup) =>
            {
                setup.ForMessage<OrderCreatedEvent>(message =>
                    message.MessageName("order-created").OnBus<OrderCreatedConsumer>()
                );
            }
        );

        // when
        await harness.Publisher.PublishAsync(new OrderCreatedEvent("ORD-001", 99.99m), cancellationToken: AbortToken);
        var recorded = await harness.WaitForConsumed<OrderCreatedEvent>(TimeSpan.FromSeconds(5), AbortToken);

        // then
        recorded.MessageId.Should().NotBeNullOrWhiteSpace();
        recorded.MessageName.Should().NotBeNullOrWhiteSpace();
        recorded.MessageType.Should().Be<OrderCreatedEvent>();
        recorded.Message.Should().BeOfType<OrderCreatedEvent>().Which.OrderId.Should().Be("ORD-001");

        harness.Published.Should().ContainSingle();
        harness.Consumed.Should().ContainSingle();
        harness.Faulted.Should().BeEmpty();
    }

    // ─── Test 2: faulted observation captures exception ───────────────────────

    [Fact]
    public async Task faulted_observation_captures_exception()
    {
        // given
        await using var harness = await _CreateHarnessAsync(
            (_, setup) =>
            {
                setup.ForMessage<OrderCreatedEvent>(message =>
                    message.MessageName("order-created").OnBus<FailingConsumer>()
                );
            }
        );

        // when
        await harness.Publisher.PublishAsync(new OrderCreatedEvent("ORD-002", 50m), cancellationToken: AbortToken);
        var faulted = await harness.WaitForFaulted<OrderCreatedEvent>(TimeSpan.FromSeconds(5), AbortToken);

        // then
        faulted.Exception.Should().NotBeNull();
        faulted.Exception.Should().BeOfType<TimeoutException>().Which.Message.Should().Be("Test failure");

        harness.Faulted.Should().ContainSingle();
        harness.Consumed.Should().BeEmpty();
    }

    // ─── Test 3: WaitForConsumed with predicate filters correctly ─────────────

    [Fact]
    public async Task wait_for_consumed_with_predicate_filters_correctly()
    {
        // given — use TestConsumer so we get a singleton that handles the messages
        await using var harness = await MessagingTestHarness.CreateAsync(
            services =>
            {
                services.AddSingleton<TestConsumer<OrderCreatedEvent>>();
                services.AddHeadlessMessaging(options =>
                {
                    options.UseInMemory();
                    options.UseInMemoryStorage();
                    options.ForMessage<OrderCreatedEvent>(message =>
                        message.MessageName("order-created").OnBus<TestConsumer<OrderCreatedEvent>>()
                    );
                });
            },
            AbortToken
        );

        // Publish a non-target message first
        await harness.Publisher.PublishAsync(new OrderCreatedEvent("other", 1m), cancellationToken: AbortToken);

        // Publish the target message
        await harness.Publisher.PublishAsync(new OrderCreatedEvent("target", 200m), cancellationToken: AbortToken);

        // when — wait for specifically the "target" order
        var recorded = await harness.WaitForConsumed<OrderCreatedEvent>(
            msg => string.Equals(msg.OrderId, "target", StringComparison.Ordinal),
            TimeSpan.FromSeconds(5),
            AbortToken
        );

        // then
        recorded.Message.Should().BeOfType<OrderCreatedEvent>().Which.OrderId.Should().Be("target");
        recorded.MessageType.Should().Be<OrderCreatedEvent>();
    }

    // ─── Test 4: timeout produces descriptive exception ───────────────────────

    [Fact]
    public async Task timeout_produces_descriptive_exception()
    {
        // given — no consumer registered; message never arrives
        await using var harness = await _CreateHarnessAsync();

        // when
        var act = async () =>
            await harness.WaitForConsumed<OrderCreatedEvent>(TimeSpan.FromMilliseconds(100), AbortToken);

        // then
        var ex = await act.Should().ThrowAsync<MessageObservationTimeoutException>();
        ex.Which.ExpectedType.Should().Be<OrderCreatedEvent>();
        ex.Which.ObservationType.Should().Be(MessageObservationType.Consumed);
        ex.Which.Message.Should().Contain(nameof(OrderCreatedEvent));
        ex.Which.Elapsed.Should().BeGreaterThan(TimeSpan.Zero);
    }

    // ─── Test 5: two harness instances do not cross-contaminate ──────────────

    [Fact]
    public async Task two_harness_instances_do_not_cross_contaminate()
    {
        // given
        await using var harness1 = await _CreateHarnessAsync(
            (_, setup) =>
            {
                setup.ForMessage<OrderCreatedEvent>(message =>
                    message.MessageName("order-created").OnBus<OrderCreatedConsumer>()
                );
            }
        );

        await using var harness2 = await _CreateHarnessAsync(
            (_, setup) =>
            {
                setup.ForMessage<OrderCreatedEvent>(message =>
                    message.MessageName("order-created").OnBus<OrderCreatedConsumer>()
                );
            }
        );

        // when — publish only in harness1
        await harness1.Publisher.PublishAsync(new OrderCreatedEvent("H1-ORD", 10m), cancellationToken: AbortToken);
        await harness1.WaitForConsumed<OrderCreatedEvent>(TimeSpan.FromSeconds(5), AbortToken);

        // then — harness2 should be untouched
        harness2.Published.Should().BeEmpty();
        harness2.Consumed.Should().BeEmpty();
        harness2.Faulted.Should().BeEmpty();
    }

    // ─── Test 6: consumer with injected dependency resolves correctly ─────────

    [Fact]
    public async Task consumer_with_injected_dependency_resolves_correctly()
    {
        // given
        var notifier = Substitute.For<INotificationService>();

        await using var harness = await MessagingTestHarness.CreateAsync(
            services =>
            {
                services.AddSingleton(notifier);
                services.AddHeadlessMessaging(options =>
                {
                    options.UseInMemory();
                    options.UseInMemoryStorage();
                    options.ForMessage<OrderCreatedEvent>(message =>
                        message.MessageName("order-created").OnBus<NotifyingConsumer>()
                    );
                });
            },
            AbortToken
        );

        // when
        await harness.Publisher.PublishAsync(new OrderCreatedEvent("ORD-INJ", 75m), cancellationToken: AbortToken);
        await harness.WaitForConsumed<OrderCreatedEvent>(TimeSpan.FromSeconds(5), AbortToken);

        // then — the mock was invoked with the correct order ID
        notifier.Received(1).Notify("ORD-INJ");
    }

    [Fact]
    public async Task bus_and_queue_observations_are_distinguished_by_intent()
    {
        // given
        await using var harness = await MessagingTestHarness.CreateAsync(
            services =>
            {
                services.AddSingleton<IntentRecorder>();
                services.AddHeadlessMessaging(options =>
                {
                    options.ForMessage<OrderCreatedEvent>(message =>
                    {
                        message
                            .MessageName("order-created")
                            .OnBus<BusIntentConsumer>(consumer => consumer.Group("bus-workers"));
                        message.OnQueue<QueueIntentConsumer>(consumer => consumer.Group("queue-workers"));
                    });
                    options.UseInMemory();
                    options.UseInMemoryStorage();
                });
            },
            AbortToken
        );

        var bus = harness.GetRequiredService<IBus>();
        var queue = harness.GetRequiredService<IQueue>();
        var recorder = harness.GetRequiredService<IntentRecorder>();
        var registeredConsumers = harness.GetRequiredService<IConsumerRegistry>().GetAll();

        registeredConsumers
            .Should()
            .Contain(c => c.ConsumerType == typeof(BusIntentConsumer) && c.IntentType == IntentType.Bus);
        registeredConsumers
            .Should()
            .Contain(c => c.ConsumerType == typeof(QueueIntentConsumer) && c.IntentType == IntentType.Queue);
        _GetInnerTransportName(harness.GetRequiredService<IQueueTransport>()).Should().Be("InMemoryQueueTransport");

        // when
        await bus.PublishAsync(
            new OrderCreatedEvent("same-payload", 10m),
            new PublishOptions { MessageName = "order-created" },
            AbortToken
        );
        await queue.EnqueueAsync(
            new OrderCreatedEvent("same-payload", 10m),
            new EnqueueOptions { MessageName = "order-created" },
            AbortToken
        );

        var busPublished = await harness.WaitForPublished<OrderCreatedEvent>(
            IntentType.Bus,
            TimeSpan.FromSeconds(5),
            AbortToken
        );
        var queuePublished = await harness.WaitForPublished<OrderCreatedEvent>(
            IntentType.Queue,
            TimeSpan.FromSeconds(5),
            AbortToken
        );
        var busConsumed = await harness.WaitForConsumed<OrderCreatedEvent>(
            IntentType.Bus,
            TimeSpan.FromSeconds(5),
            AbortToken
        );
        var queueConsumed = await harness.WaitForConsumed<OrderCreatedEvent>(
            IntentType.Queue,
            TimeSpan.FromSeconds(5),
            AbortToken
        );

        // then
        busPublished.IntentType.Should().Be(IntentType.Bus);
        queuePublished.IntentType.Should().Be(IntentType.Queue);
        busConsumed.IntentType.Should().Be(IntentType.Bus);
        queueConsumed.IntentType.Should().Be(IntentType.Queue);
        recorder.Intents.Should().BeEquivalentTo([IntentType.Bus, IntentType.Queue]);
    }

    [Fact]
    public async Task outbox_bus_and_queue_flow_through_inmemory_transport_with_intent()
    {
        // given
        await using var harness = await MessagingTestHarness.CreateAsync(
            services =>
            {
                services.AddSingleton<IntentRecorder>();
                services.AddHeadlessMessaging(options =>
                {
                    options.ForMessage<OrderCreatedEvent>(message =>
                    {
                        message
                            .MessageName("outbox-order-created")
                            .OnBus<BusIntentConsumer>(consumer => consumer.Group("outbox-bus"));
                        message.OnQueue<QueueIntentConsumer>(consumer => consumer.Group("outbox-queue"));
                    });
                    options.UseInMemory();
                    options.UseInMemoryStorage();
                });
            },
            AbortToken
        );

        var outboxBus = harness.GetRequiredService<IOutboxBus>();
        var outboxQueue = harness.GetRequiredService<IOutboxQueue>();
        var recorder = harness.GetRequiredService<IntentRecorder>();

        // when
        await outboxBus.PublishAsync(
            new OrderCreatedEvent("outbox-bus", 10m),
            new PublishOptions { MessageName = "outbox-order-created" },
            AbortToken
        );
        await outboxQueue.EnqueueAsync(
            new OrderCreatedEvent("outbox-queue", 20m),
            new EnqueueOptions { MessageName = "outbox-order-created" },
            AbortToken
        );

        var busPublished = await harness.WaitForPublished<OrderCreatedEvent>(
            IntentType.Bus,
            TimeSpan.FromSeconds(5),
            AbortToken
        );
        var queuePublished = await harness.WaitForPublished<OrderCreatedEvent>(
            IntentType.Queue,
            TimeSpan.FromSeconds(5),
            AbortToken
        );
        var busConsumed = await harness.WaitForConsumed<OrderCreatedEvent>(
            message => string.Equals(message.OrderId, "outbox-bus", StringComparison.Ordinal),
            TimeSpan.FromSeconds(5),
            AbortToken
        );
        var queueConsumed = await harness.WaitForConsumed<OrderCreatedEvent>(
            message => string.Equals(message.OrderId, "outbox-queue", StringComparison.Ordinal),
            TimeSpan.FromSeconds(5),
            AbortToken
        );

        // then
        busPublished.IntentType.Should().Be(IntentType.Bus);
        queuePublished.IntentType.Should().Be(IntentType.Queue);
        busConsumed.IntentType.Should().Be(IntentType.Bus);
        queueConsumed.IntentType.Should().Be(IntentType.Queue);
        recorder.Intents.Should().Contain([IntentType.Bus, IntentType.Queue]);
    }

    [Fact]
    public async Task queue_observation_is_not_double_recorded_as_bus_for_queue_transport()
    {
        // given
        await using var harness = await MessagingTestHarness.CreateAsync(
            services =>
            {
                services.AddHeadlessMessaging(options =>
                {
                    options.UseInMemoryStorage();
                    options.RegisterExtension(new QueueOnlyTransportExtension());
                });
            },
            AbortToken
        );

        var queue = harness.GetRequiredService<IQueue>();

        // when
        await queue.EnqueueAsync(
            new OrderCreatedEvent("queue-only", 10m),
            new EnqueueOptions { MessageName = "queue-only-order-created" },
            AbortToken
        );

        // then
        var published = await harness.WaitForPublished<OrderCreatedEvent>(
            IntentType.Queue,
            TimeSpan.FromSeconds(5),
            AbortToken
        );
        published.IntentType.Should().Be(IntentType.Queue);
        harness.Published.Should().ContainSingle();
        harness.Published.Should().NotContain(message => message.IntentType == IntentType.Bus);
    }

    private static string? _GetInnerTransportName(object transport)
    {
        return transport
            .GetType()
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Select(field => field.GetValue(transport)?.GetType().Name)
            .FirstOrDefault(name => name?.Contains("Transport", StringComparison.Ordinal) == true);
    }

    private sealed class QueueOnlyTransportExtension : IMessagesOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton(new MessageQueueMarkerService("QueueOnly"));
            services.AddSingleton<IQueueTransport, SuccessfulQueueTransport>();
            services.AddSingleton<IConsumerClientFactory, UnusedConsumerClientFactory>();
        }
    }

    private sealed class SuccessfulQueueTransport : IQueueTransport
    {
        public BrokerAddress BrokerAddress => new("QueueOnly", "localhost");

        public Task<OperateResult> SendAsync(TransportMessage message, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperateResult.Success);
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class UnusedConsumerClientFactory : IConsumerClientFactory
    {
        public Task<IConsumerClient> CreateAsync(
            string groupName,
            byte groupConcurrent,
            CancellationToken cancellationToken = default
        )
        {
            throw new InvalidOperationException("The queue-only transport harness test registers no consumers.");
        }
    }

    // ─── Test 7: AddMessagingTestHarness registers into existing container ───

    [Fact]
    public async Task add_messaging_test_harness_registers_into_existing_container()
    {
        // given — simulate a host-owned ServiceCollection
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(options =>
        {
            options.UseInMemory();
            options.UseInMemoryStorage();
            options.ForMessage<OrderCreatedEvent>(message =>
                message.MessageName("order-created").OnBus<OrderCreatedConsumer>()
            );
        });

        // Register the test harness via extension method (hosted mode)
        services.AddMessagingTestHarness();

        await using var sp = services.BuildServiceProvider();

        // Bootstrap manually (in a real host, the HostedService does this)
        var bootstrapper = sp.GetRequiredService<IBootstrapper>();
        await bootstrapper.BootstrapAsync(AbortToken);

        var harness = sp.GetRequiredService<MessagingTestHarness>();

        // when
        var publisher = sp.GetRequiredService<IBus>();
        await publisher.PublishAsync(new OrderCreatedEvent("HOSTED-1", 42m), cancellationToken: AbortToken);
        var recorded = await harness.WaitForConsumed<OrderCreatedEvent>(TimeSpan.FromSeconds(5), AbortToken);

        // then — harness observes messages through the same container
        recorded.Message.Should().BeOfType<OrderCreatedEvent>().Which.OrderId.Should().Be("HOSTED-1");
        harness.Published.Should().ContainSingle();
        harness.Consumed.Should().ContainSingle();
    }

    // ─── Test 8: WaitForExhausted observes user OnExhausted callback ──────────

    [Fact]
    public async Task wait_for_exhausted_observes_user_callback_invocation()
    {
        // given — single-attempt budget so failure becomes terminal immediately
        var userCallbackFired = 0;

        await using var harness = await _CreateHarnessAsync(
            (services, options) =>
            {
                options.ForMessage<OrderCreatedEvent>(message =>
                    message.MessageName("order-created").OnBus<FailingConsumer>()
                );
                options.Options.RetryPolicy.MaxPersistedRetries = 0;
                options.Options.RetryPolicy.RetryStrategy = new RetryStrategyOptions
                {
                    MaxRetryAttempts = 0,
                    Delay = TimeSpan.Zero,
                    ShouldHandle = static _ => ValueTask.FromResult(true),
                };
                options.Options.RetryPolicy.OnExhausted = (_, _) =>
                {
                    Interlocked.Increment(ref userCallbackFired);
                    return Task.CompletedTask;
                };
            }
        );

        // when
        await harness.Publisher.PublishAsync(new OrderCreatedEvent("ORD-EXH", 1m), cancellationToken: AbortToken);
        var recorded = await harness.WaitForExhausted<OrderCreatedEvent>(TimeSpan.FromSeconds(5), AbortToken);

        // then — observation captures the exhausted message and its exception
        recorded.MessageType.Should().Be<OrderCreatedEvent>();
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
    public async Task wait_for_exhausted_records_before_user_callback_runs()
    {
        // given — user callback hangs; observation must still be visible
        using var hangGate = new ManualResetEventSlim(initialState: false);

        await using var harness = await _CreateHarnessAsync(
            (services, options) =>
            {
                options.ForMessage<OrderCreatedEvent>(message =>
                    message.MessageName("order-created").OnBus<FailingConsumer>()
                );
                options.Options.RetryPolicy.MaxPersistedRetries = 0;
                options.Options.RetryPolicy.RetryStrategy = new RetryStrategyOptions
                {
                    MaxRetryAttempts = 0,
                    Delay = TimeSpan.Zero,
                    ShouldHandle = static _ => ValueTask.FromResult(true),
                };
                options.Options.RetryPolicy.OnExhaustedTimeout = TimeSpan.FromMilliseconds(200);
                options.Options.RetryPolicy.OnExhausted = async (_, ct) =>
                {
                    // Park the callback until the test releases it (or the timeout fires).
                    await Task.Run(() => hangGate.Wait(ct), ct);
                };
            }
        );

        try
        {
            // when
            await harness.Publisher.PublishAsync(new OrderCreatedEvent("ORD-HANG", 1m), cancellationToken: AbortToken);
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
        var deadline = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(20);
        }
    }
}
