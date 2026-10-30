// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Messaging.Transport;
using Microsoft.Extensions.DependencyInjection;
using Tests.Simulation;

namespace Tests;

/// <summary>A second queue message, so a host runs two subscriptions whose clients a test can tell apart.</summary>
public sealed record PipelineRefund(string Id)
{
    public const string Name = "tests.pipeline.refund";
}

/// <summary>
/// Consumes orders and refunds under one identity, so the host runs two subscriptions that share one consumer circuit.
/// </summary>
[QueueConsumer(Identity)]
public sealed class LedgerConsumer(PipelineProbe probe, TimeProvider clock)
    : IConsume<PipelineOrder>,
        IConsume<PipelineRefund>
{
    public const string Identity = "tests.restart.ledger";

    public ValueTask ConsumeAsync(ConsumeContext<PipelineOrder> context, CancellationToken cancellationToken)
    {
        return probe.HandleAsync(context.Message.Id, clock.GetUtcNow(), cancellationToken);
    }

    public ValueTask ConsumeAsync(ConsumeContext<PipelineRefund> context, CancellationToken cancellationToken)
    {
        return probe.HandleAsync(context.Message.Id, clock.GetUtcNow(), cancellationToken);
    }
}

/// <summary>
/// Starts simulation hosts that run <see cref="LedgerConsumer"/> on two subscriptions, with a client factory a test
/// can hold or fail per subscription.
/// </summary>
internal static class LedgerHosts
{
    public static Task<SimulationHost> StartAsync(
        PipelineProbe probe,
        CancellationToken cancellationToken,
        Action<MessagingSetupBuilder>? configure = null
    )
    {
        var broker = new SimulatedBroker(new SimulationClock());
        var factory = new ControlledClientFactory(broker);

        return SimulationHost.StartAsync(
            setup =>
            {
                setup.AddConsumer<LedgerConsumer>();
                setup.Options.RetryProcessor.AdaptivePolling = false;
                setup.Options.RetryProcessor.BaseInterval = PipelineHosts.PollInterval;

                // Registered after the simulated broker's own extension, so this factory replaces its client factory.
                setup.RegisterExtension(new ControlledClientFactoryExtension(factory));
                configure?.Invoke(setup);
            },
            services =>
            {
                services.AddSingleton(probe);
                services.ConfigureMessaging(messaging =>
                {
                    messaging.Message<PipelineOrder>(PipelineOrder.Name);
                    messaging.Message<PipelineRefund>(PipelineRefund.Name);
                });
            },
            broker,
            cancellationToken
        );
    }

    public static ControlledClientFactory ClientFactory(this SimulationHost host) =>
        (ControlledClientFactory)host.GetRequiredService<IConsumerClientFactory>();

    public static IConsumerRegister Register(this SimulationHost host) => host.GetRequiredService<IConsumerRegister>();

    public static string OrderSubscription(this SimulationHost host) =>
        host.Broker.SubscriptionsFor(MessageLane.Queue, PipelineOrder.Name).Should().ContainSingle().Subject;

    public static string RefundSubscription(this SimulationHost host) =>
        host.Broker.SubscriptionsFor(MessageLane.Queue, PipelineRefund.Name).Should().ContainSingle().Subject;

    public static async Task EnqueueRefundAsync(
        this SimulationHost host,
        string id,
        CancellationToken cancellationToken
    )
    {
        await using var scope = host.Services.CreateAsyncScope();
        await scope
            .ServiceProvider.GetRequiredService<IQueue>()
            .EnqueueAsync(new PipelineRefund(id), options: null, cancellationToken)
            .ConfigureAwait(false);
    }

    public static CircuitBreakerSnapshot LedgerCircuit(this SimulationHost host) =>
        host.GetRequiredService<ICircuitBreakerMonitor>().GetSnapshot(MessageLane.Queue, LedgerConsumer.Identity)
        ?? throw new InvalidOperationException($"{LedgerConsumer.Identity} has no circuit.");

    /// <summary>Raises <paramref name="logType"/> from <paramref name="client"/>, as its transport would.</summary>
    public static void Report(this SimulatedConsumerClient client, MqLogType logType)
    {
        client.OnLogCallback.Should().NotBeNull("the register attaches its callbacks before the client listens");
        client.OnLogCallback!(new LogMessageEventArgs { LogType = logType, Reason = $"simulated {logType}" });
    }

    private sealed class ControlledClientFactoryExtension(ControlledClientFactory factory) : IMessagesOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton<IConsumerClientFactory>(factory);
        }
    }
}

/// <summary>
/// Creates the simulated broker's consumer clients, and lets a test hold or fail one creation for a subscription and
/// count the creations each subscription asked for.
/// </summary>
internal sealed class ControlledClientFactory(SimulatedBroker broker) : IConsumerClientFactory
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, int> _created = new(StringComparer.Ordinal);
    private readonly List<(string Subscription, int Ordinal, Func<CancellationToken, Task> Action)> _scripted = [];
    private Func<IConsumerClient, IConsumerClient>? _wrap;

    /// <summary>Wraps every client created from now on, to give it a transport's threading behavior.</summary>
    public void WrapClients(Func<IConsumerClient, IConsumerClient> wrap)
    {
        Volatile.Write(ref _wrap, wrap);
    }

    /// <summary>Gets how many clients <paramref name="subscription"/> asked for, its probe clients included.</summary>
    public int CreatedFor(string subscription)
    {
        lock (_lock)
        {
            return _created.GetValueOrDefault(subscription);
        }
    }

    /// <summary>
    /// Holds the creation for <paramref name="subscription"/> that follows its next <paramref name="skip"/> ones until
    /// <see cref="CreationHold.Release"/> or the caller cancels it.
    /// </summary>
    public CreationHold Hold(string subscription, int skip)
    {
        var hold = new CreationHold();
        _Script(subscription, skip, hold.WaitAsync);
        return hold;
    }

    /// <summary>Fails the creation for <paramref name="subscription"/> that follows its next <paramref name="skip"/> ones.</summary>
    public void Fail(string subscription, int skip, Exception exception)
    {
        _Script(subscription, skip, _ => Task.FromException(exception));
    }

    public async Task<IConsumerClient> CreateAsync(
        ConsumerClientRequest request,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        Func<CancellationToken, Task>? action = null;
        lock (_lock)
        {
            var ordinal = _created.GetValueOrDefault(request.SubscriptionName) + 1;
            _created[request.SubscriptionName] = ordinal;

            var index = _scripted.FindIndex(x =>
                string.Equals(x.Subscription, request.SubscriptionName, StringComparison.Ordinal)
                && x.Ordinal == ordinal
            );
            if (index >= 0)
            {
                action = _scripted[index].Action;
                _scripted.RemoveAt(index);
            }
        }

        if (action is not null)
        {
            await action(cancellationToken).ConfigureAwait(false);
        }

        var client = broker.CreateConsumerClient(request);
        return Volatile.Read(ref _wrap) is { } wrap ? wrap(client) : client;
    }

    private void _Script(string subscription, int skip, Func<CancellationToken, Task> action)
    {
        lock (_lock)
        {
            _scripted.Add((subscription, _created.GetValueOrDefault(subscription) + skip + 1, action));
        }
    }
}

/// <summary>One held client creation: entered once the register asks for it, ended by a release or a cancellation.</summary>
internal sealed class CreationHold
{
    private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _canceled;

    /// <summary>Completes once the register asked for the held client.</summary>
    public Task Entered => _entered.Task;

    /// <summary>Gets whether the register canceled the held creation instead of waiting for it.</summary>
    public bool WasCanceled => Volatile.Read(ref _canceled) == 1;

    public void Release() => _released.TrySetResult();

    internal async Task WaitAsync(CancellationToken cancellationToken)
    {
        _entered.TrySetResult();
        try
        {
            await _released.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Volatile.Write(ref _canceled, 1);
            throw;
        }
    }
}
