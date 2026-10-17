// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Globalization;
using Headless.Context;
using Headless.Coordination;
using Headless.Messaging;
using Headless.Messaging.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Testcontainers.RabbitMq;
using Tests.Ledger;

namespace Tests.Stack;

/// <summary>
/// One scenario run against a <see cref="ChaosStackFixture"/>: the hosts it starts, the ledger they share, and the
/// names that keep it apart from every other run on the same broker and database.
/// </summary>
/// <remarks>
/// Each run gets its own message-name prefix, exchange, and messaging <see cref="MessagingOptions.Version"/>, so its
/// queues, rows, retry pickup, and locks never meet an earlier run's leftovers.
/// </remarks>
public sealed class ChaosRun : IAsyncDisposable
{
    /// <summary>How often a coordinated host beats; the suspicion and dead thresholds follow from it.</summary>
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// The dispatch lease. It must outlast one publish's whole inline burst: three sends of
    /// <see cref="TransportPublishTimeout"/> plus their back-off.
    /// </summary>
    public static readonly TimeSpan DispatchTimeout = TimeSpan.FromSeconds(20);

    public static readonly TimeSpan TransportPublishTimeout = TimeSpan.FromSeconds(3);

    private readonly ChaosStackFixture _stack;
    private readonly ILoggerProvider _logs;
    private readonly List<ChaosHost> _hosts = [];

    // Bounds the publishes in flight, so a stalled store or broker queues them here instead of opening a connection each.
    private readonly SemaphoreSlim _publishGate = new(64);
    private ServiceProvider? _inspector;
    private int _hostSeed;

    public ChaosRun(ChaosStackFixture stack, ChaosSettings settings, ILoggerProvider logs)
    {
        _stack = stack;
        Settings = settings;
        _logs = logs;
        var id = Guid.NewGuid().ToString("N")[..10];
        Prefix = "c" + id;
        Version = "c" + id;
        Ledger = new MessageLedger(TimeProvider.System);
    }

    public string Prefix { get; }

    public string Version { get; }

    public ChaosSettings Settings { get; }

    public MessageLedger Ledger { get; }

    public IReadOnlyList<ChaosHost> Hosts => _hosts;

    /// <summary>Gets the work queue as the broker names it.</summary>
    public string WorkQueue => $"queue.{QualifiedName(ChaosModule.WorkName)}";

    /// <summary>Gets the queue the broker dead-letters work messages into.</summary>
    public string WorkDeadLetterQueue => WorkQueue + ".dlq";

    public string QualifiedName(string messageName) => $"{Prefix}.{messageName}";

    public string Key(int index) => string.Create(CultureInfo.InvariantCulture, $"{Prefix}-{index:D6}");

    /// <summary>Builds and starts a host; it reaches the broker and database the way the settings say unless told otherwise.</summary>
    public async Task<ChaosHost> StartHostAsync(
        HostBehavior behavior,
        bool? brokerThroughProxy = null,
        bool? storageThroughProxy = null,
        CancellationToken cancellationToken = default
    )
    {
        var host = new ChaosHost(
            behavior,
            _BuildHost(
                behavior,
                brokerThroughProxy ?? Settings.BrokerThroughProxy,
                storageThroughProxy ?? Settings.StorageThroughProxy
            )
        );
        _hosts.Add(host);
        await host.StartAsync(cancellationToken).ConfigureAwait(false);

        return host;
    }

    /// <summary>A fresh behavior with its own random stream, so two runs with one seed pick the same work durations.</summary>
    public HostBehavior Behavior(
        string name,
        TimeSpan minWork = default,
        TimeSpan maxWork = default,
        bool failFirst = false
    ) =>
        new(name, Interlocked.Increment(ref _hostSeed))
        {
            MinWork = minWork,
            MaxWork = maxWork,
            FailFirstAttempt = failFirst,
        };

    /// <summary>Publishes one work message durably and records whether the stack accepted it.</summary>
    public async Task<bool> PublishWorkAsync(
        ChaosHost host,
        string key,
        TimeSpan? delay = null,
        CancellationToken cancellationToken = default
    )
    {
        var publishedAt = Ledger.Now;

        try
        {
            await host
                .Queue.EnqueueAsync(
                    new ChaosWork(key),
                    new QueueOptions
                    {
                        MessageName = ChaosModule.WorkName,
                        MessageId = key,
                        DeliveryMode = DeliveryMode.Durable,
                        Delay = delay,
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // A refused publish is part of the scenario, such as one made while storage is down; the ledger keeps it
            // apart from accepted messages, which alone are owed a run.
            Ledger.RecordPublishRejected(key);
            return false;
        }

        Ledger.RecordPublished(key, host.Name, delay is { } d ? publishedAt + d : null);
        return true;
    }

    /// <summary>Publishes <paramref name="count"/> work messages from <paramref name="host"/>, a bounded number at a time.</summary>
    public Task PublishBacklogAsync(ChaosHost host, int count, CancellationToken cancellationToken) =>
        Task.WhenAll(
            Enumerable.Range(0, count).Select(index => _PublishGatedAsync(host, Key(index), cancellationToken))
        );

    /// <summary>
    /// Publishes work messages at <paramref name="perSecond"/> for <paramref name="duration"/>, from the hosts
    /// <paramref name="pickHost"/> chooses, without letting a slow or failing publish hold the pace back.
    /// </summary>
    /// <returns>How many publishes were attempted.</returns>
    public async Task<int> PublishAtRateAsync(
        int perSecond,
        TimeSpan duration,
        Func<int, ChaosHost> pickHost,
        CancellationToken cancellationToken
    )
    {
        const int ticksPerSecond = 10;
        var started = Stopwatch.GetTimestamp();
        var pending = new List<Task>();
        var index = 0;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1.0 / ticksPerSecond));

        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            var elapsed = Stopwatch.GetElapsedTime(started);
            var target = (int)(Math.Min(elapsed.TotalSeconds, duration.TotalSeconds) * perSecond);

            // Catches up to the rate on every tick, so a stalled tick does not lower the total.
            while (index < target)
            {
                var current = index++;
                pending.Add(_PublishGatedAsync(pickHost(current), Key(current), cancellationToken));
            }

            if (elapsed >= duration)
            {
                break;
            }
        }

        await Task.WhenAll(pending).ConfigureAwait(false);
        return index;
    }

    /// <summary>Counts the received rows of <paramref name="messageName"/> in <paramref name="status"/>.</summary>
    public async Task<long> CountReceivedAsync(
        string messageName,
        StatusName status,
        CancellationToken cancellationToken
    )
    {
        var page = await _Monitoring()
            .GetMessagesAsync(
                new MessageQuery
                {
                    MessageType = MessageType.Subscribe,
                    Name = QualifiedName(messageName),
                    StatusName = status,
                    CurrentPage = 0,
                    PageSize = 1,
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        return page.TotalItems;
    }

    /// <summary>Returns the published rows of <paramref name="messageName"/> whose lease is still in the future.</summary>
    public async Task<IReadOnlyList<MessageView>> FindLeasedPublishedRowsAsync(
        string messageName,
        CancellationToken cancellationToken
    )
    {
        const int pageSize = 500;
        var monitoring = _Monitoring();
        var now = Ledger.Now;
        var leased = new List<MessageView>();

        for (var page = 0; ; page++)
        {
            var rows = await monitoring
                .GetMessagesAsync(
                    new MessageQuery
                    {
                        MessageType = MessageType.Publish,
                        Name = QualifiedName(messageName),
                        CurrentPage = page,
                        PageSize = pageSize,
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);

            leased.AddRange(rows.Items.Where(row => row.LockedUntil > now));

            if (rows.Items.Count < pageSize)
            {
                return leased;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var host in _hosts)
        {
            // A host cut off from its database may fail its last writes; the run is over, so that is not a finding.
            try
            {
                await host.StopAsync().ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                TestContext.Current.TestOutputHelper?.WriteLine($"Stopping host {host.Name} failed: {exception}");
            }
        }

        await _stack.RestoreNetworkAsync().ConfigureAwait(false);

        if (_inspector is not null)
        {
            await _inspector.DisposeAsync().ConfigureAwait(false);
        }

        _publishGate.Dispose();
    }

    private async Task _PublishGatedAsync(ChaosHost host, string key, CancellationToken cancellationToken)
    {
        await _publishGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await PublishWorkAsync(host, key, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _publishGate.Release();
        }
    }

    private IHost _BuildHost(HostBehavior behavior, bool brokerThroughProxy, bool storageThroughProxy)
    {
        var storage = storageThroughProxy ? _stack.ProxiedStorageConnectionString : _stack.StorageConnectionString;
        var (brokerHost, brokerPort) = brokerThroughProxy
            ? (_stack.BrokerProxy.Host, _stack.BrokerProxy.Port)
            : (_stack.BrokerHost, _stack.BrokerPort);

        var builder = Host.CreateEmptyApplicationBuilder(settings: null);
        var services = builder.Services;

        services.AddLogging(logging =>
        {
            logging.AddProvider(_logs);
            logging.SetMinimumLevel(LogLevel.Warning);
        });
        services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(30));
        services.AddSingleton(Ledger);
        services.AddSingleton(behavior);

        if (Settings.Coordination)
        {
            services.AddHeadlessHostIdentity(options => options.HostName = behavior.Name);
            services.AddHeadlessCoordination(setup =>
            {
                _stack.UseCoordination(setup, storage);
                setup.Configure(options =>
                {
                    options.ClusterName = Prefix;
                    options.HeartbeatInterval = HeartbeatInterval;
                    options.SuspicionThreshold = HeartbeatInterval * 3;
                    // No lower than the dispatch lease, as the dead-owner recovery guidance asks: a node declared
                    // dead has then let every lease it held run out.
                    options.DeadThreshold = DispatchTimeout;
                    options.DeadRetentionWindow = TimeSpan.FromMinutes(1);
                    options.MembershipLostBehavior = MembershipLostBehavior.StopMembershipOnly;
                });
            });
        }

        services.AddHeadlessMessaging(setup =>
        {
            setup.UseRabbitMq(rabbitMq =>
            {
                rabbitMq.HostName = brokerHost;
                rabbitMq.Port = brokerPort;
                rabbitMq.UserName = RabbitMqBuilder.DefaultUsername;
                rabbitMq.Password = RabbitMqBuilder.DefaultPassword;
                rabbitMq.ExchangeName = Prefix;
                rabbitMq.QueueArguments.QueueType = "quorum";
                rabbitMq.QueueArguments.DeliveryLimit = Settings.DeliveryLimit;
                rabbitMq.QueueArguments.EnableDeadLettering = true;
                // A dead link is noticed within seconds instead of the client's one-minute default.
                rabbitMq.ConnectionFactoryOptions = factory => factory.RequestedHeartbeat = TimeSpan.FromSeconds(10);
            });
            _stack.UseStorage(setup, storage);

            var options = setup.Options;
            options.MessageNamePrefix = Prefix;
            options.Version = Version;
            options.MinimumInboxGuarantee = InboxGuarantee.Durable;
            options.ConsumerThreadCount = Settings.ClientsPerHost;
            options.RetryBatchSize = Settings.RetryBatchSize;
            options.ShutdownTimeout = Settings.ShutdownTimeout;
            options.DelayedMessagePollInterval = Settings.DelayedPollInterval;
            options.TransportPublishTimeout = TransportPublishTimeout;
            options.DeadNodeReconcileInterval = TimeSpan.FromSeconds(5);
            options.RetryProcessor.BaseInterval = TimeSpan.FromSeconds(1);
            options.RetryPolicy.DispatchTimeout = DispatchTimeout;
            options.RetryPolicy.InitialDispatchGrace = TimeSpan.FromSeconds(10);
            options.RetryPolicy.OnExhausted = (failed, _) =>
            {
                if (failed.Message.Headers.TryGetValue(Headers.MessageId, out var messageId) && messageId is not null)
                {
                    Ledger.RecordExhausted(messageId);
                }

                return Task.CompletedTask;
            };

            setup.Tune(
                ChaosModule.WorkConsumer,
                consumer =>
                {
                    consumer.Concurrency(Settings.WorkConcurrency).FailurePolicy(Settings.WorkFailurePolicy);

                    if (Settings.WorkCircuitBreaker is { } circuitBreaker)
                    {
                        consumer.CircuitBreaker(circuitBreaker);
                    }
                }
            );
            Settings.ConfigureMessaging?.Invoke(setup);
        });
        services.ConfigureMessaging(messaging =>
        {
            messaging.Message<ChaosWork>(ChaosModule.WorkName);
            messaging.Message<ChaosRequest>(ChaosModule.RequestName);
            messaging.Message<ChaosReply>(ChaosModule.ReplyName);
            messaging.AddModule<ChaosModule>();
        });

        _DecorateQueueTransport(services, behavior.Name);

        return builder.Build();
    }

    private void _DecorateQueueTransport(IServiceCollection services, string hostName)
    {
        var registered = services.Last(descriptor => descriptor.ServiceType == typeof(IQueueTransport));
        services.Remove(registered);
        services.AddSingleton<IQueueTransport>(provider => new LedgerTransport(
            (IQueueTransport)(
                registered.ImplementationInstance
                ?? registered.ImplementationFactory?.Invoke(provider)
                ?? ActivatorUtilities.CreateInstance(provider, registered.ImplementationType!)
            ),
            Ledger,
            hostName,
            Settings.Sabotage
        ));
    }

    private IMonitoringApi _Monitoring()
    {
        // A storage-only view that outlives every host, so a scenario can read the rows after its last host stopped.
        _inspector ??= _BuildInspector();
        return _inspector.GetRequiredService<IDataStorage>().GetMonitoringApi();
    }

    private ServiceProvider _BuildInspector()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseInMemory();
            _stack.UseStorage(setup, _stack.StorageConnectionString);
            setup.Options.Version = Version;
            setup.Options.MessageNamePrefix = Prefix;
        });

        return services.BuildServiceProvider();
    }
}
