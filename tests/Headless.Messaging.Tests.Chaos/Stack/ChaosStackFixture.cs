// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Coordination;
using Headless.Messaging;
using Headless.Testing.Testcontainers;
using RabbitMQ.Client;
using Tests.Toxiproxy;

namespace Tests.Stack;

/// <summary>
/// One broker, one database, and a Toxiproxy server with a route to each, shared by every scenario of a database. A
/// host reaches the broker and the database either directly or through its route, so a scenario can cut, black-hole,
/// or flap a link while the containers keep running.
/// </summary>
public abstract class ChaosStackFixture : IAsyncLifetime
{
    private const string _BlackholeToxic = "chaos-blackhole";
    private static readonly TimeSpan _StorageRouteTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan _StorageRouteProbeInterval = TimeSpan.FromMilliseconds(250);

    private readonly RabbitMqComponent _rabbitMq = new();
    private readonly ToxiproxyServer _toxiproxy = new();
    private bool _blackholed;

    /// <summary>Gets the database name the summary and test names use.</summary>
    public abstract string Database { get; }

    public ToxiproxyProxy BrokerProxy
    {
        get => field ?? throw new InvalidOperationException("The stack has not started.");
        private set;
    }

    public ToxiproxyProxy StorageProxy
    {
        get => field ?? throw new InvalidOperationException("The stack has not started.");
        private set;
    }

    public string BrokerHost => _rabbitMq.HostName;

    public int BrokerPort => _rabbitMq.Port;

    public string BrokerUserName => _rabbitMq.UserName;

    public string BrokerPassword => _rabbitMq.Password;

    public string BrokerVirtualHost => _rabbitMq.VirtualHost;

    /// <summary>Gets the connection string that reaches the database directly.</summary>
    public abstract string StorageConnectionString { get; }

    /// <summary>Gets the connection string that reaches the database through <see cref="StorageProxy"/>.</summary>
    public abstract string ProxiedStorageConnectionString { get; }

    protected abstract IAsyncLifetime StorageComponent { get; }

    /// <summary>Gets the database port the Docker host publishes, which the storage route forwards to.</summary>
    protected abstract int StorageHostPort { get; }

    public abstract void UseStorage(MessagingSetupBuilder setup, string connectionString);

    public abstract void UseCoordination(HeadlessCoordinationSetupBuilder setup, string connectionString);

    public async ValueTask InitializeAsync()
    {
        await ((IAsyncLifetime)_rabbitMq).InitializeAsync();
        await StorageComponent.InitializeAsync();
        await _toxiproxy.StartAsync();
        BrokerProxy = await _toxiproxy.CreateProxyAsync("broker", BrokerPort);
        StorageProxy = await _toxiproxy.CreateProxyAsync("storage", StorageHostPort);
    }

    public async ValueTask DisposeAsync()
    {
        await _toxiproxy.DisposeAsync();
        await StorageComponent.DisposeAsync();
        await ((IAsyncLifetime)_rabbitMq).DisposeAsync();
        GC.SuppressFinalize(this);
    }

    /// <summary>Holds the broker link open but drops everything the broker sends back, as a broker that stops answering.</summary>
    public async Task BlackholeBrokerAsync(CancellationToken cancellationToken)
    {
        await BrokerProxy
            .AddToxicAsync(
                _BlackholeToxic,
                "timeout",
                new Dictionary<string, object> { ["timeout"] = 0 },
                cancellationToken
            )
            .ConfigureAwait(false);
        _blackholed = true;
    }

    public async Task RestoreBrokerAnswersAsync(CancellationToken cancellationToken)
    {
        if (!_blackholed)
        {
            return;
        }

        // A reset also enables the storage route; no scenario cuts storage while the broker is blackholed.
        await BrokerProxy.ResetServerAsync(cancellationToken).ConfigureAwait(false);
        _blackholed = false;
    }

    /// <summary>
    /// Puts both routes back to healthy, whatever an interrupted scenario left behind, and returns once the storage
    /// route opens a new connection again.
    /// </summary>
    public async Task RestoreNetworkAsync()
    {
        await RestoreBrokerAnswersAsync(CancellationToken.None).ConfigureAwait(false);
        await BrokerProxy.EnableAsync(CancellationToken.None).ConfigureAwait(false);
        await StorageProxy.EnableAsync(CancellationToken.None).ConfigureAwait(false);

        // A driver can keep the faults of the cut it just saw: SqlClient's pool replays a failed open for up to a
        // minute, so the next scenario's first host would fail at its schema step on a fault that is already over.
        ClearStoragePools();
        await _WaitUntilStorageRouteOpensAsync().ConfigureAwait(false);
    }

    /// <summary>Drops every pooled storage connection, with any failure state the driver keeps per pool.</summary>
    protected abstract void ClearStoragePools();

    /// <summary>Opens and closes one unpooled connection through <see cref="StorageProxy"/>.</summary>
    protected abstract Task OpenUnpooledProxiedConnectionAsync(CancellationToken cancellationToken);

    /// <summary>Counts the messages a queue holds, read straight from the broker.</summary>
    public async Task<uint> CountQueuedAsync(string queue, CancellationToken cancellationToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = BrokerHost,
            Port = BrokerPort,
            UserName = BrokerUserName,
            Password = BrokerPassword,
            VirtualHost = BrokerVirtualHost,
        };

        await using var connection = await factory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var channel = await connection
            .CreateChannelAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var declared = await channel.QueueDeclarePassiveAsync(queue, cancellationToken).ConfigureAwait(false);

        return declared.MessageCount;
    }

    // The proxy acknowledges an enable before the container runtime's port forwarder reaches its listener again, so a
    // first connect can still be reset; only a connection that opened proves the route is back.
    private async Task _WaitUntilStorageRouteOpensAsync()
    {
        // wall-clock: the probe's deadline bounds a real network wait, so it is measured on the monotonic clock
        var started = TimeProvider.System.GetTimestamp();

        while (true)
        {
            try
            {
                await OpenUnpooledProxiedConnectionAsync(CancellationToken.None).ConfigureAwait(false);
                return;
            }
            catch (DbException) when (TimeProvider.System.GetElapsedTime(started) < _StorageRouteTimeout)
            {
                // real-wait: the port forwarder recovers on its own real-time schedule, which no test clock drives
                await Task.Delay(_StorageRouteProbeInterval).ConfigureAwait(false);
            }
        }
    }

    private sealed class RabbitMqComponent : HeadlessRabbitMqFixture
    {
        // A chaos run measures recovery under load, so its broker keeps a server no other run shares.
        protected override bool ShareContainer => false;
    }
}
