// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Reflection;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tests.Helpers;

namespace Tests.EveryInstance;

/// <summary>
/// Covers every-instance Bus delivery on a host running the in-memory transport: one client per process, a delivery
/// path that never touches storage, commit-and-log on a consumer failure, the capability gate for declared and runtime
/// consumers, and the subscription-established hook.
/// </summary>
public sealed class EveryInstanceDeliveryTests : TestBase
{
    [Fact]
    public async Task should_open_one_every_instance_client_per_process_when_consumer_thread_count_is_greater_than_one()
    {
        // given
        var factory = new RecordingFactory();
        await using var provider = _BuildHost(
            factory,
            configure: services =>
                services.ConfigureMessaging(m =>
                {
                    m.AddModule<PriceCacheModule>();
                    m.Message<StockChanged>("tests.stock-changed");
                }),
            configureMessaging: setup =>
            {
                setup.Options.ConsumerThreadCount = 3;
                setup.AddConsumer<StockProjection>();
            }
        );

        // when
        await provider.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);

        // then
        var instanceId = provider.GetRequiredService<MessagingInstanceId>().Value;
        var everyInstance = factory.Requests.Where(x => x.Kind is ConsumerSubscriptionKind.EveryInstance).ToList();
        everyInstance.Should().NotBeEmpty();
        everyInstance.Should().AllSatisfy(x => x.SubscriptionName.Should().Be(PriceCache.Identity));
        everyInstance.Should().AllSatisfy(x => x.InstanceId.Should().Be(instanceId));
        factory
            .SubscribedClients(PriceCache.Identity)
            .Should()
            .Be(1, "an every-instance subscription belongs to one process");
        factory
            .SubscribedClients(
                provider
                    .GetDrainedConsumerRegistry()
                    .GetAll()
                    .Single(x => x.ConsumerType == typeof(StockProjection))
                    .SubscriptionName
            )
            .Should()
            .Be(3, "competing subscriptions keep one client per consumer thread");
    }

    [Fact]
    public async Task should_deliver_every_instance_message_without_touching_received_storage()
    {
        // given
        var factory = new RecordingFactory();
        var storage = new StorageCallLog();
        await using var provider = _BuildHost(
            factory,
            configure: services =>
            {
                services.ConfigureMessaging(m => m.AddModule<PriceCacheModule>());
                _Decorate<IDataStorage>(services, inner => StorageRecorder.Wrap(inner, storage));
            }
        );
        await provider.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);
        var probe = provider.GetRequiredService<EveryInstanceProbe>();

        // when
        await provider
            .GetRequiredService<IBus>()
            .PublishAsync(new PriceChanged("sku-1"), cancellationToken: AbortToken);
        await _WaitUntilAsync(() => probe.Consumed.Count == 1);

        // then
        probe.Consumed.Should().Equal("sku-1");
        storage.ReceivedWrites.Should().BeEmpty("an every-instance delivery keeps no inbox or received row");
        await _WaitUntilAsync(() => factory.Commits(PriceCache.Identity) == 1);
        factory.Rejects(PriceCache.Identity).Should().Be(0);
    }

    [Fact]
    public async Task should_commit_and_count_a_failed_every_instance_delivery_without_retry()
    {
        // given
        var factory = new RecordingFactory();
        var storage = new StorageCallLog();
        var outcomes = new ConcurrentBag<string>();
        using var listener = _ListenToEveryInstanceOutcomes(PriceCache.Identity, outcomes);
        await using var provider = _BuildHost(
            factory,
            configure: services =>
            {
                services.ConfigureMessaging(m => m.AddModule<PriceCacheModule>());
                _Decorate<IDataStorage>(services, inner => StorageRecorder.Wrap(inner, storage));
            }
        );
        await provider.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);
        var probe = provider.GetRequiredService<EveryInstanceProbe>();
        var bus = provider.GetRequiredService<IBus>();

        // when
        await bus.PublishAsync(new PriceChanged(PriceCache.FailingSku), cancellationToken: AbortToken);
        await bus.PublishAsync(new PriceChanged("sku-2"), cancellationToken: AbortToken);
        await _WaitUntilAsync(() =>
            probe.Consumed.Contains("sku-2", StringComparer.Ordinal) && factory.Commits(PriceCache.Identity) == 2
        );

        // then
        probe.Consumed.Should().Equal(PriceCache.FailingSku, "sku-2");
        factory
            .Rejects(PriceCache.Identity)
            .Should()
            .Be(0, "a failed every-instance delivery is committed, not requeued");
        storage.ReceivedWrites.Should().BeEmpty("a failed every-instance delivery keeps no retry row");
        outcomes.Should().BeEquivalentTo(["failed", "succeeded"]);
    }

    [Fact]
    public async Task should_reject_every_instance_consumer_at_startup_before_any_client_on_a_transport_without_support()
    {
        // given
        var factory = new RecordingFactory();
        await using var provider = _BuildHost(
            factory,
            configure: services =>
            {
                services.ConfigureMessaging(m => m.AddModule<PriceCacheModule>());
                _DeclareTransportWithoutEveryInstance(services);
            }
        );

        // when
        var act = () => provider.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);

        // then
        (await act.Should().ThrowAsync<MessagingConfigurationException>()).WithMessage(
            $"*'{PriceCache.Identity}'*'{_UnsupportingProvider}'*"
        );
        factory.Requests.Should().BeEmpty("the gate runs before any consumer client, and so any broker object, exists");
    }

    [Fact]
    public async Task should_reject_an_every_instance_runtime_subscription_on_a_transport_without_support()
    {
        // given
        var factory = new RecordingFactory();
        await using var provider = _BuildHost(factory, configure: _DeclareTransportWithoutEveryInstance);
        await provider.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);
        var subscriber = provider.GetRequiredService<IRuntimeSubscriber>();

        // when
        var act = async () =>
            await subscriber.SubscribeAsync<PriceChanged>(
                (_, _, _) => ValueTask.CompletedTask,
                new RuntimeSubscriptionOptions { HandlerId = "tests.runtime-price", EveryInstance = true },
                AbortToken
            );

        // then
        (await act.Should().ThrowAsync<MessagingConfigurationException>()).WithMessage($"*'{_UnsupportingProvider}'*");
        factory.Requests.Should().BeEmpty();
        provider.GetRequiredService<IRuntimeConsumerRegistry>().GetDescriptors().Should().BeEmpty();
    }

    [Fact]
    public async Task should_name_an_every_instance_runtime_subscription_after_its_explicit_identity()
    {
        // given
        var factory = new RecordingFactory();
        await using var provider = _BuildHost(factory);
        await provider.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);

        // when
        await using var handle = await provider
            .GetRequiredService<IRuntimeSubscriber>()
            .SubscribeAsync<PriceChanged>(
                (_, _, _) => ValueTask.CompletedTask,
                new RuntimeSubscriptionOptions
                {
                    HandlerId = "tests.runtime-price",
                    Identity = "tests.shared-price",
                    EveryInstance = true,
                },
                AbortToken
            );

        // then: the identity names the subscription, and each process still derives its own broker object from it.
        handle.Identity.Should().Be("tests.shared-price");
        factory
            .Requests.Where(x => x.Kind is ConsumerSubscriptionKind.EveryInstance)
            .Select(x => x.SubscriptionName)
            .Distinct(StringComparer.Ordinal)
            .Should()
            .Equal("tests.shared-price");
    }

    [Fact]
    public async Task should_deliver_an_every_instance_runtime_subscription_through_its_own_client_without_storage()
    {
        // given
        var factory = new RecordingFactory();
        var storage = new StorageCallLog();
        await using var provider = _BuildHost(
            factory,
            configure: services => _Decorate<IDataStorage>(services, inner => StorageRecorder.Wrap(inner, storage))
        );
        await provider.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);
        var received = new ConcurrentQueue<string>();

        // when
        await using var handle = await provider
            .GetRequiredService<IRuntimeSubscriber>()
            .SubscribeAsync<PriceChanged>(
                (context, _, _) =>
                {
                    received.Enqueue(context.Message.Sku);
                    return ValueTask.CompletedTask;
                },
                new RuntimeSubscriptionOptions { HandlerId = "tests.runtime-price", EveryInstance = true },
                AbortToken
            );
        await provider
            .GetRequiredService<IBus>()
            .PublishAsync(new PriceChanged("sku-9"), cancellationToken: AbortToken);
        await _WaitUntilAsync(() => received.Count == 1);

        // then
        received.Should().Equal("sku-9");
        factory
            .Requests.Where(x => x.Kind is ConsumerSubscriptionKind.EveryInstance)
            .Should()
            .OnlyContain(x => x.SubscriptionName == handle.Identity);
        storage.ReceivedWrites.Should().BeEmpty();
    }

    [Fact]
    public async Task should_call_the_subscription_hook_on_start_on_rebuild_and_on_a_client_reported_reconnect()
    {
        // given
        var factory = new RecordingFactory();
        await using var provider = _BuildHost(
            factory,
            configure: services => services.ConfigureMessaging(m => m.AddModule<PriceCacheModule>())
        );
        var probe = provider.GetRequiredService<EveryInstanceProbe>();

        // when: the host starts
        await provider.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);

        // then
        probe.Established.Should().ContainSingle();
        probe
            .Established.Should()
            .HaveElementAt(
                0,
                new SubscriptionEstablishedContext(PriceCache.Identity, probe.Established[0].MessageNames, false, 1)
            );
        probe.Established[0].MessageNames.Should().ContainSingle().Which.Should().EndWith(nameof(PriceChanged));

        // when: the core rebuilds its clients, as after a broker failure
        await provider.GetRequiredService<IConsumerRegister>().ReStartAsync(force: true, AbortToken);

        // then
        await _WaitUntilAsync(() => probe.Established.Count == 2);
        probe.Established[1].IsReconnect.Should().BeTrue();
        probe.Established[1].Generation.Should().Be(2);

        // when: the live client reports that it recovered its subscription on its own
        await factory.LatestClient(PriceCache.Identity).RaiseReestablishedAsync(AbortToken);

        // then
        probe.Established.Should().HaveCount(3);
        probe.Established[2].IsReconnect.Should().BeTrue();
        probe.Established[2].Generation.Should().Be(3);
    }

    [Fact]
    public async Task should_not_call_the_subscription_hook_of_a_competing_consumer()
    {
        // given
        var factory = new RecordingFactory();
        await using var provider = _BuildHost(
            factory,
            configure: services => services.ConfigureMessaging(m => m.AddModule<CompetingPriceCacheModule>())
        );

        // when
        await provider.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);

        // then
        provider.GetRequiredService<EveryInstanceProbe>().Established.Should().BeEmpty();
        factory.Requests.Should().OnlyContain(x => x.Kind == ConsumerSubscriptionKind.Competing);
    }

    [Fact]
    public async Task should_not_require_an_inbox_tier_for_a_host_whose_only_consumers_are_every_instance()
    {
        // given
        var factory = new RecordingFactory();
        await using var provider = _BuildHost(
            factory,
            configure: services => services.ConfigureMessaging(m => m.AddModule<PriceCacheModule>()),
            configureMessaging: setup =>
                setup.Options.RequiredInboxCapability = MessagingInboxCapabilityTier.Transactional
        );

        // when
        var act = () => provider.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);

        // then
        await act.Should().NotThrowAsync("every-instance consumers keep no inbox state");
    }

    private const string _UnsupportingProvider = "NoEveryInstance";

    private ServiceProvider _BuildHost(
        RecordingFactory factory,
        Action<IServiceCollection>? configure = null,
        Action<MessagingSetupBuilder>? configureMessaging = null
    )
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(LoggerProvider));
        services.AddSingleton<EveryInstanceProbe>();
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseInMemory();
            setup.UseProcessLocalInMemoryStorage();
            setup.Options.RequiredInboxCapability = MessagingInboxCapabilityTier.ProcessLocal;
            configureMessaging?.Invoke(setup);
        });
        configure?.Invoke(services);
        _Decorate<IConsumerClientFactory>(services, factory.Wrap);

        return services.BuildServiceProvider();
    }

    private static void _DeclareTransportWithoutEveryInstance(IServiceCollection services)
    {
        var declared = services
            .Where(x =>
                x.ImplementationInstance is MessagingProviderCapabilities { Role: MessagingProviderRole.Transport }
            )
            .ToList();
        foreach (var descriptor in declared)
        {
            services.Remove(descriptor);
        }

        services.AddMessagingProviderCapabilities(
            MessagingProviderCapabilities.Transport(
                _UnsupportingProvider,
                [MessageLane.Bus, MessageLane.Queue],
                supportsIndependentLaneTopology: true
            )
        );
    }

    private static void _Decorate<TService>(IServiceCollection services, Func<TService, TService> wrap)
        where TService : class
    {
        var inner = services.Last(x => x.ServiceType == typeof(TService));
        services.Remove(inner);
        services.AddSingleton(sp =>
            wrap(
                (TService)(
                    inner.ImplementationInstance
                    ?? inner.ImplementationFactory?.Invoke(sp)
                    ?? ActivatorUtilities.CreateInstance(sp, inner.ImplementationType!)
                )
            )
        );
    }

    private static MeterListener _ListenToEveryInstanceOutcomes(string consumerIdentity, ConcurrentBag<string> outcomes)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (
                    string.Equals(instrument.Meter.Name, MessagingDiagnostics.SourceName, StringComparison.Ordinal)
                    && string.Equals(
                        instrument.Name,
                        MessagingMetrics.EveryInstanceDeliveriesName,
                        StringComparison.Ordinal
                    )
                )
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>(
            (_, _, tags, _) =>
            {
                string? identity = null;
                string? outcome = null;
                foreach (var tag in tags)
                {
                    if (string.Equals(tag.Key, MessagingMetrics.TagConsumerGroupName, StringComparison.Ordinal))
                    {
                        identity = tag.Value as string;
                    }
                    else if (string.Equals(tag.Key, MessagingMetrics.TagEveryInstanceOutcome, StringComparison.Ordinal))
                    {
                        outcome = tag.Value as string;
                    }
                }

                if (string.Equals(identity, consumerIdentity, StringComparison.Ordinal) && outcome is not null)
                {
                    outcomes.Add(outcome);
                }
            }
        );
        listener.Start();
        return listener;
    }

    private static async Task _WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (!condition())
        {
            await Task.Delay(20, timeout.Token);
        }
    }

    /// <summary>Records every consumer client request and wraps each client so a test can count settlements.</summary>
    private sealed class RecordingFactory
    {
        private readonly ConcurrentQueue<ConsumerClientRequest> _requests = new();
        private readonly ConcurrentQueue<RecordingClient> _clients = new();

        public IReadOnlyList<ConsumerClientRequest> Requests => [.. _requests];

        public IConsumerClientFactory Wrap(IConsumerClientFactory inner) => new Recorder(this, inner);

        public int SubscribedClients(string subscriptionName) =>
            _clients.Count(x =>
                x.Subscribed && string.Equals(x.Request.SubscriptionName, subscriptionName, StringComparison.Ordinal)
            );

        public int Commits(string subscriptionName) => _For(subscriptionName).Sum(x => x.CommitCount);

        public int Rejects(string subscriptionName) => _For(subscriptionName).Sum(x => x.RejectCount);

        public RecordingClient LatestClient(string subscriptionName) => _For(subscriptionName).Last(x => x.Subscribed);

        private IEnumerable<RecordingClient> _For(string subscriptionName) =>
            _clients.Where(x => string.Equals(x.Request.SubscriptionName, subscriptionName, StringComparison.Ordinal));

        private sealed class Recorder(RecordingFactory owner, IConsumerClientFactory inner) : IConsumerClientFactory
        {
            public async Task<IConsumerClient> CreateAsync(
                ConsumerClientRequest request,
                CancellationToken cancellationToken = default
            )
            {
                owner._requests.Enqueue(request);
                var client = new RecordingClient(request, await inner.CreateAsync(request, cancellationToken));
                owner._clients.Enqueue(client);
                return client;
            }
        }
    }

    /// <summary>Forwards to the transport's client and counts what the core asked of it.</summary>
    private sealed class RecordingClient(ConsumerClientRequest request, IConsumerClient inner) : IConsumerClient
    {
        private int _commits;
        private int _rejects;
        private Func<CancellationToken, Task>? _onReestablished;

        public ConsumerClientRequest Request { get; } = request;

        public bool Subscribed { get; private set; }

        public int CommitCount => Volatile.Read(ref _commits);

        public int RejectCount => Volatile.Read(ref _rejects);

        public BrokerAddress BrokerAddress => inner.BrokerAddress;

        public Func<TransportMessage, object?, Task>? OnMessageCallback => inner.OnMessageCallback;

        public Action<LogMessageEventArgs>? OnLogCallback => inner.OnLogCallback;

        public Task RaiseReestablishedAsync(CancellationToken cancellationToken) =>
            (_onReestablished ?? throw new InvalidOperationException("No re-established callback is attached."))(
                cancellationToken
            );

        public ValueTask ShutdownAsync(TimeSpan timeout, CancellationToken cancellationToken = default) =>
            inner.ShutdownAsync(timeout, cancellationToken);

        public ValueTask<ICollection<string>> FetchMessageNamesAsync(
            IEnumerable<string> messageNames,
            CancellationToken cancellationToken = default
        ) => inner.FetchMessageNamesAsync(messageNames, cancellationToken);

        public ValueTask SubscribeAsync(IEnumerable<string> messageNames, CancellationToken cancellationToken = default)
        {
            Subscribed = true;
            return inner.SubscribeAsync(messageNames, cancellationToken);
        }

        public ValueTask ListeningAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
            inner.ListeningAsync(timeout, cancellationToken);

        public ValueTask WaitUntilReadyAsync(CancellationToken cancellationToken = default) =>
            inner.WaitUntilReadyAsync(cancellationToken);

        public ValueTask CommitAsync(object? sender, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _commits);
            return inner.CommitAsync(sender, cancellationToken);
        }

        public ValueTask RejectAsync(object? sender, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _rejects);
            return inner.RejectAsync(sender, cancellationToken);
        }

        public ValueTask PauseAsync(CancellationToken cancellationToken = default) =>
            inner.PauseAsync(cancellationToken);

        public ValueTask ResumeAsync(CancellationToken cancellationToken = default) =>
            inner.ResumeAsync(cancellationToken);

        public void AttachCallbacks(
            Func<TransportMessage, object?, Task>? onMessage,
            Action<LogMessageEventArgs>? onLog
        ) => inner.AttachCallbacks(onMessage, onLog);

        public void AttachReestablishedCallback(Func<CancellationToken, Task>? onReestablished) =>
            _onReestablished = onReestablished;

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    /// <summary>The storage calls a received delivery writes through, recorded by name.</summary>
    internal sealed class StorageCallLog
    {
        private static readonly FrozenSet<string> _ReceivedWriteNames = new[]
        {
            nameof(IDataStorage.StoreReceivedMessageAsync),
            nameof(IDataStorage.StoreReceivedExceptionMessageAsync),
            nameof(IDataStorage.AdmitReceivedMessageAsync),
        }.ToFrozenSet(StringComparer.Ordinal);

        private readonly ConcurrentQueue<string> _calls = new();

        public IReadOnlyList<string> ReceivedWrites => [.. _calls.Where(_ReceivedWriteNames.Contains)];

        public void Record(string name) => _calls.Enqueue(name);
    }

    public class StorageRecorder : DispatchProxy
    {
        private IDataStorage _inner = null!;
        private StorageCallLog _log = null!;

        internal static IDataStorage Wrap(IDataStorage inner, StorageCallLog log)
        {
            var proxy = Create<IDataStorage, StorageRecorder>();
            var recorder = (StorageRecorder)(object)proxy;
            recorder._inner = inner;
            recorder._log = log;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            _log.Record(targetMethod!.Name);
            try
            {
                return targetMethod.Invoke(_inner, args);
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(ex.InnerException);
                throw;
            }
        }
    }
}

public sealed record PriceChanged(string Sku);

public sealed record StockChanged(string Sku);

/// <summary>Records what the every-instance test consumers saw, per host.</summary>
public sealed class EveryInstanceProbe
{
    private readonly Lock _lock = new();
    private readonly List<string> _consumed = [];
    private readonly List<SubscriptionEstablishedContext> _established = [];

    public IReadOnlyList<string> Consumed
    {
        get
        {
            lock (_lock)
            {
                return [.. _consumed];
            }
        }
    }

    public IReadOnlyList<SubscriptionEstablishedContext> Established
    {
        get
        {
            lock (_lock)
            {
                return [.. _established];
            }
        }
    }

    public void Consume(string sku)
    {
        lock (_lock)
        {
            _consumed.Add(sku);
        }
    }

    public void Establish(SubscriptionEstablishedContext context)
    {
        lock (_lock)
        {
            _established.Add(context);
        }
    }
}

public sealed class PriceCache(EveryInstanceProbe probe) : IConsume<PriceChanged>, IOnSubscriptionEstablished
{
    public const string Identity = "tests.price-cache";
    public const string FailingSku = "sku-fails";

    public ValueTask ConsumeAsync(ConsumeContext<PriceChanged> context, CancellationToken cancellationToken)
    {
        probe.Consume(context.Message.Sku);

        return string.Equals(context.Message.Sku, FailingSku, StringComparison.Ordinal)
            ? throw new InvalidOperationException("The price cache could not apply the change.")
            : ValueTask.CompletedTask;
    }

    public ValueTask OnSubscriptionEstablishedAsync(
        SubscriptionEstablishedContext context,
        CancellationToken cancellationToken
    )
    {
        probe.Establish(context);
        return ValueTask.CompletedTask;
    }
}

[BusConsumer("tests.stock")]
public sealed class StockProjection : IConsume<StockChanged>
{
    public ValueTask ConsumeAsync(ConsumeContext<StockChanged> context, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;
}

public sealed class PriceCacheModule : IMessagingModule
{
    public static void Register(MessagingCatalogBuilder catalog) =>
        catalog.AddBusConsumer<PriceCache, PriceChanged>(
            PriceCache.Identity,
            everyInstance: true,
            policy: null,
            Tests.Registration.TestConsumers.Dispatch<PriceCache, PriceChanged>()
        );
}

public sealed class CompetingPriceCacheModule : IMessagingModule
{
    public static void Register(MessagingCatalogBuilder catalog) =>
        catalog.AddBusConsumer<PriceCache, PriceChanged>(
            PriceCache.Identity,
            everyInstance: false,
            policy: null,
            Tests.Registration.TestConsumers.Dispatch<PriceCache, PriceChanged>()
        );
}
