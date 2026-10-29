// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using DotNet.Testcontainers.Containers;
using Headless.Hosting;
using Headless.Messaging;
using Headless.Messaging.RabbitMq;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tests.Helpers;

namespace Tests;

/// <summary>
/// A host running two queue consumers on one RabbitMQ broker, driven by its own transport check: when the broker
/// cancels one consumer, the next check rebuilds only that consumer's subscription, and a consumer the circuit breaker
/// paused is not rebuilt at all. The other consumer keeps the broker consumer it started with throughout.
/// </summary>
[Collection<RabbitMqFixture>]
public sealed class RabbitMqConsumerCancelRecoveryTests(RabbitMqFixture fixture) : TestBase
{
    private static readonly TimeSpan _Wait = TimeSpan.FromSeconds(20);

    // The transport check runs every 30 seconds; this covers one full interval after any moment of the test.
    private static readonly TimeSpan _PastTransportCheck = TimeSpan.FromSeconds(45);

    // Longer than one transport-check interval, so a check is certain to run while the circuit holds the consumer.
    private static readonly TimeSpan _OpenDuration = TimeSpan.FromSeconds(45);

    // rabbitmqctl occasionally stalls without answering, so each call is bounded and a polling wait asks again.
    private static readonly TimeSpan _BrokerQueryTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task should_rebuild_only_the_cancelled_consumer_and_keep_the_other_one_when_the_broker_cancels_it()
    {
        // given: both consumers are registered on the broker
        await using var host = await _StartHostAsync(configureCircuit: null);
        var cancelledBefore = await _WaitForConsumersAsync(host.CancelledQueue);
        var survivorBefore = await _WaitForConsumersAsync(host.SurvivorQueue);

        // when: an operator deletes the first consumer's queue, which makes the broker cancel its consumer, then
        // deploys the topology again
        await fixture.DeleteQueueAsOperatorAsync(host.CancelledQueue, AbortToken);
        await host.Services.ProvisionResourcesAsync(AbortToken);

        // then: nothing but the transport check restores the cancelled consumer, and the other one consumes meanwhile
        (await _ConsumersOfAsync(host.CancelledQueue))
            .Should()
            .BeEmpty();
        await host.EnqueueSurvivorAsync("survivor-during", AbortToken);
        await host.Survivor.WaitForAsync("survivor-during", _Wait, AbortToken);

        // and: the next transport check gives the cancelled queue a new consumer
        var cancelledAfter = await _WaitForConsumersAsync(host.CancelledQueue, _PastTransportCheck);
        cancelledAfter.Should().NotIntersectWith(cancelledBefore);
        host.LoggedEvent("TransportUnhealthy")
            .Should()
            .BeTrue("only the transport check rebuilds a cancelled consumer");

        // and: both consume, and the other queue still has the broker consumer, on the same channel, it started with
        await host.EnqueueCancelledAsync("cancelled-after", AbortToken);
        await host.EnqueueSurvivorAsync("survivor-after", AbortToken);
        await host.Cancelled.WaitForAsync("cancelled-after", _Wait, AbortToken);
        await host.Survivor.WaitForAsync("survivor-after", _Wait, AbortToken);
        (await _ConsumersOfAsync(host.SurvivorQueue)).Should().BeEquivalentTo(survivorBefore);
    }

    [Fact]
    public async Task should_not_rebuild_a_consumer_the_circuit_breaker_paused_when_a_transport_check_runs()
    {
        // given: one failure opens the first consumer's circuit, for longer than one transport-check interval
        await using var host = await _StartHostAsync(circuit =>
        {
            circuit.FailureThreshold = 1;
            circuit.OpenDuration = _OpenDuration;
            circuit.MaxOpenDuration = _OpenDuration;
        });
        var pausedBefore = await _WaitForConsumersAsync(host.CancelledQueue);
        var survivorBefore = await _WaitForConsumersAsync(host.SurvivorQueue);
        host.Cancelled.FailFirstDelivery = true;

        // when: a delivery fails, so the circuit opens and the pause cancels the consumer on the broker
        await host.EnqueueCancelledAsync("cancelled-fails", AbortToken);
        await _WaitUntilAsync(
            async () =>
            {
                try
                {
                    return (await _ConsumersOfAsync(host.CancelledQueue)).Count == 0;
                }
                catch (TimeoutException)
                {
                    return false;
                }
            },
            _Wait,
            "the open circuit paused the consumer"
        );
        var checksBeforePause = host.CountEvents("TransportChecking");

        // and: a transport check runs while the circuit still holds the consumer
        await _WaitUntilAsync(
            () => Task.FromResult(host.CountEvents("TransportChecking") > checksBeforePause),
            _PastTransportCheck,
            "a transport check ran during the pause"
        );
        (await _ConsumersOfAsync(host.CancelledQueue)).Should().BeEmpty("the circuit is still open");

        // then: once the circuit half-opens the consumer resumes on the channel it had, so nothing rebuilt it
        var resumed = await _WaitForConsumersAsync(host.CancelledQueue, _OpenDuration + _Wait);
        resumed.Select(static c => c.Channel).Should().BeEquivalentTo(pausedBefore.Select(static c => c.Channel));
        host.LoggedEvent("TransportUnhealthy").Should().BeFalse("a paused consumer's cancel is not a failure");

        // and: both consume, and the other queue kept its broker consumer
        await host.Cancelled.WaitForAsync("cancelled-fails", _Wait, AbortToken);
        await host.EnqueueCancelledAsync("cancelled-after", AbortToken);
        await host.EnqueueSurvivorAsync("survivor-after", AbortToken);
        await host.Cancelled.WaitForAsync("cancelled-after", _Wait, AbortToken);
        await host.Survivor.WaitForAsync("survivor-after", _Wait, AbortToken);
        (await _ConsumersOfAsync(host.SurvivorQueue)).Should().BeEquivalentTo(survivorBefore);
    }

    private async Task<CancelHost> _StartHostAsync(Action<CircuitBreakerOptions>? configureCircuit)
    {
        var cancelledName = $"cancel-x-{Guid.NewGuid():N}";
        var survivorName = $"cancel-y-{Guid.NewGuid():N}";
        var exchange = $"cancel-{Guid.NewGuid():N}";
        var log = new List<(LogLevel Level, EventId EventId, string Message)>();
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.AddProvider(LoggerProvider);
            builder.AddProvider(new CapturingLoggerProvider(log));
            builder.SetMinimumLevel(LogLevel.Debug);
        });
        services.AddSingleton<CancelledConsumer>();
        services.AddSingleton<SurvivorConsumer>();
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseRabbitMq(options =>
            {
                options.HostName = fixture.HostName;
                options.Port = fixture.Port;
                options.UserName = fixture.UserName;
                options.Password = fixture.Password;
                options.ExchangeName = exchange;
            });
            setup.UseInMemoryStorage();
            setup.Options.MinimumInboxGuarantee = InboxGuarantee.ProcessLocal;
            configureCircuit?.Invoke(setup.Options.CircuitBreaker);
        });
        services.ConfigureMessaging(messaging =>
        {
            messaging.Message<CancelledRun>(cancelledName);
            messaging.Message<SurvivorRun>(survivorName);
            messaging.AddModule<CancelRecoveryModule>();
        });

        var provider = services.BuildServiceProvider();

        try
        {
            await provider.SetupResourcesAndBootstrapAsync(AbortToken);
            return new CancelHost(
                provider,
                log,
                RabbitMqPhysicalAddress.Queue(MessageLane.Queue, CancelRecoveryModule.CancelledIdentity, cancelledName),
                RabbitMqPhysicalAddress.Queue(MessageLane.Queue, CancelRecoveryModule.SurvivorIdentity, survivorName)
            );
        }
        catch
        {
            await provider.DisposeAsync();
            throw;
        }
    }

    private async Task<IReadOnlyList<BrokerConsumer>> _WaitForConsumersAsync(string queueName, TimeSpan? timeout = null)
    {
        IReadOnlyList<BrokerConsumer> consumers = [];
        await _WaitUntilAsync(
            async () =>
            {
                try
                {
                    consumers = await _ConsumersOfAsync(queueName);
                }
                catch (TimeoutException)
                {
                    return false;
                }

                return consumers.Count > 0;
            },
            timeout ?? _Wait,
            $"queue '{queueName}' has a consumer"
        );

        return consumers;
    }

    // The broker's own view of a queue's consumers: the channel each one is on and the tag it was registered under. A
    // rebuilt client opens a new channel, so an unchanged channel and tag mean the same client kept consuming.
    private async Task<IReadOnlyList<BrokerConsumer>> _ConsumersOfAsync(string queueName)
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        bounded.CancelAfter(_BrokerQueryTimeout);
        ExecResult result;

        try
        {
            result = await fixture.Container.ExecAsync(
                [
                    "rabbitmqctl",
                    "list_consumers",
                    "-q",
                    "--no-table-headers",
                    "queue_name",
                    "channel_pid",
                    "consumer_tag",
                ],
                bounded.Token
            );
        }
        catch (OperationCanceledException e) when (!AbortToken.IsCancellationRequested)
        {
            throw new TimeoutException($"rabbitmqctl list_consumers did not answer within {_BrokerQueryTimeout}.", e);
        }

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"rabbitmqctl list_consumers failed ({result.ExitCode}): {result.Stderr}"
            );
        }

        return result
            .Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static line => line.Split('\t'))
            .Where(columns => columns.Length == 3 && string.Equals(columns[0], queueName, StringComparison.Ordinal))
            .Select(static columns => new BrokerConsumer(columns[1], columns[2]))
            .ToArray();
    }

    private static async Task _WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout, string description)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        deadline.CancelAfter(timeout);

        while (!await condition())
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), deadline.Token);
            }
            catch (OperationCanceledException e) when (!AbortToken.IsCancellationRequested)
            {
                throw new TimeoutException($"Timed out after {timeout} waiting until {description}.", e);
            }
        }
    }

    private sealed record BrokerConsumer(string Channel, string Tag);

    private sealed class CancelHost(
        ServiceProvider services,
        List<(LogLevel Level, EventId EventId, string Message)> log,
        string cancelledQueue,
        string survivorQueue
    ) : IAsyncDisposable
    {
        public IServiceProvider Services => services;

        public CancelledConsumer Cancelled { get; } = services.GetRequiredService<CancelledConsumer>();

        public SurvivorConsumer Survivor { get; } = services.GetRequiredService<SurvivorConsumer>();

        public string CancelledQueue => cancelledQueue;

        public string SurvivorQueue => survivorQueue;

        public int CountEvents(string eventName)
        {
            lock (log)
            {
                return log.Count(entry => string.Equals(entry.EventId.Name, eventName, StringComparison.Ordinal));
            }
        }

        public bool LoggedEvent(string eventName) => CountEvents(eventName) > 0;

        public Task EnqueueCancelledAsync(string id, CancellationToken cancellationToken) =>
            _EnqueueAsync(new CancelledRun(id), cancellationToken);

        public Task EnqueueSurvivorAsync(string id, CancellationToken cancellationToken) =>
            _EnqueueAsync(new SurvivorRun(id), cancellationToken);

        public ValueTask DisposeAsync() => services.DisposeAsync();

        private async Task _EnqueueAsync<TMessage>(TMessage message, CancellationToken cancellationToken)
            where TMessage : class
        {
            await using var scope = services.CreateAsyncScope();
            await scope
                .ServiceProvider.GetRequiredService<IQueue>()
                .EnqueueAsync(message, options: null, cancellationToken);
        }
    }

    public sealed record CancelledRun(string Id);

    public sealed record SurvivorRun(string Id);

    public sealed class CancelRecoveryModule : IMessagingModule
    {
        public const string CancelledIdentity = "tests.rabbitmq.cancel-recovery.cancelled";
        public const string SurvivorIdentity = "tests.rabbitmq.cancel-recovery.survivor";

        public static void Register(MessagingCatalogBuilder catalog)
        {
            catalog.AddQueueConsumer<CancelledConsumer, CancelledRun>(
                CancelledIdentity,
                TestConsumerDispatch.FromServices<CancelledConsumer, CancelledRun>()
            );
            catalog.AddQueueConsumer<SurvivorConsumer, SurvivorRun>(
                SurvivorIdentity,
                TestConsumerDispatch.FromServices<SurvivorConsumer, SurvivorRun>()
            );
        }
    }

    public sealed class CancelledConsumer : IConsume<CancelledRun>
    {
        private int _deliveries;

        public RunLedger Ledger { get; } = new();

        /// <summary>Gets or sets whether the first delivery fails the way a timed-out dependency does.</summary>
        public bool FailFirstDelivery { get; set; }

        public ValueTask ConsumeAsync(ConsumeContext<CancelledRun> context, CancellationToken cancellationToken)
        {
            if (FailFirstDelivery && Interlocked.Increment(ref _deliveries) == 1)
            {
                throw new TimeoutException("The dependency timed out.");
            }

            Ledger.Add(context.Message.Id);
            return ValueTask.CompletedTask;
        }

        public Task WaitForAsync(string id, TimeSpan timeout, CancellationToken cancellationToken) =>
            Ledger.WaitForAsync(id, timeout, cancellationToken);
    }

    public sealed class SurvivorConsumer : IConsume<SurvivorRun>
    {
        public RunLedger Ledger { get; } = new();

        public ValueTask ConsumeAsync(ConsumeContext<SurvivorRun> context, CancellationToken cancellationToken)
        {
            Ledger.Add(context.Message.Id);
            return ValueTask.CompletedTask;
        }

        public Task WaitForAsync(string id, TimeSpan timeout, CancellationToken cancellationToken) =>
            Ledger.WaitForAsync(id, timeout, cancellationToken);
    }

    /// <summary>The ids a consumer handled, each completing the wait a test started for it.</summary>
    public sealed class RunLedger
    {
        private readonly ConcurrentDictionary<string, TaskCompletionSource> _handled = new(StringComparer.Ordinal);

        public void Add(string id) => _Entry(id).TrySetResult();

        public async Task WaitForAsync(string id, TimeSpan timeout, CancellationToken cancellationToken)
        {
            try
            {
                await _Entry(id).Task.WaitAsync(timeout, cancellationToken);
            }
            catch (TimeoutException e)
            {
                throw new TimeoutException($"'{id}' was not handled within {timeout}.", e);
            }
        }

        private TaskCompletionSource _Entry(string id) =>
            _handled.GetOrAdd(
                id,
                static _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
            );
    }
}
