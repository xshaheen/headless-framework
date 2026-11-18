// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;
using Tests.Toxiproxy;

namespace Tests.Cluster;

/// <summary>
/// One node slot of a cluster: its routes and whichever host incarnation runs in it now. A slot can be started,
/// stopped gracefully, killed, frozen, partitioned, and refilled with a fresh incarnation without touching any other
/// slot.
/// </summary>
public sealed class ClusterNode
{
    private const string _FreezeToxic = "chaos-freeze";

    // A killed host's stop runs on a cancelled token; this only bounds a stop that hangs anyway.
    private static readonly TimeSpan _AbandonTimeout = TimeSpan.FromSeconds(30);

    private readonly JobsCluster _cluster;
    private readonly NodeRoutes _routes;
    private Running? _current;
    private int _incarnations;

    internal ClusterNode(JobsCluster cluster, int slot, NodeRoutes routes)
    {
        _cluster = cluster;
        _routes = routes;
        Slot = slot;
    }

    public int Slot { get; }

    public string Name => $"node-{Slot.ToString(CultureInfo.InvariantCulture)}";

    public bool IsRunning => _current is not null;

    /// <summary>The running incarnation, such as <c>node-0#2</c>.</summary>
    public string Runner => _Current.Incarnation.Runner;

    /// <summary>The owner tag the running incarnation stamps rows with, or null once it lost its identity.</summary>
    public string? OwnerTag => _current?.Incarnation.Membership.GetOwnerTag();

    /// <summary>When the running incarnation's membership-loss token fired, on the ledger clock.</summary>
    public TimeSpan? MembershipLostAt => _current?.MembershipLostAt;

    /// <summary>Whether the running incarnation has crossed its self-fence, which no later heartbeat undoes.</summary>
    public bool IsFenced =>
        _current is { } current
        && (
            current.Incarnation.Membership.Identity is null
            || current.Incarnation.Membership.LocalMembershipLostToken.IsCancellationRequested
        );

    private ToxiproxyProxy CoordinationRoute =>
        _cluster.Coordination == ClusterCoordination.Redis ? _routes.Redis : _routes.Coordination;

    private Running _Current => _current ?? throw new InvalidOperationException($"{Name} is not running.");

    /// <summary>Starts a fresh incarnation once every route it uses answers.</summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_current is not null)
        {
            throw new InvalidOperationException($"{Name} is already running {Runner}.");
        }

        var number = ++_incarnations;
        var application = $"{_cluster.Name}-{Name}-{number.ToString(CultureInfo.InvariantCulture)}";

        // Every route of this slot up and free of toxics. The previous incarnation is fully disposed and its pools
        // cleared by now, so nothing of it can reconnect through the re-enabled routes.
        await _routes.Server.ResetAsync(cancellationToken).ConfigureAwait(false);

        var jobs = _cluster.Store.Proxied(_cluster.ConnectionString, _routes.Jobs, application);
        string? coordination = null;
        string? redis = null;
        await _cluster.WaitUntilAnswersAsync(
            () => _cluster.Store.ProbeAsync(jobs, cancellationToken),
            cancellationToken
        );

        if (_cluster.Coordination == ClusterCoordination.Redis)
        {
            redis = $"{_routes.Redis.Host}:{_routes.Redis.Port.ToString(CultureInfo.InvariantCulture)}";
            await _cluster.WaitUntilAnswersAsync(() => JobsCluster.PingRedisAsync(redis), cancellationToken);
        }
        else
        {
            coordination = _cluster.Store.Proxied(
                _cluster.ConnectionString,
                _routes.Coordination,
                application + "-coordination"
            );
            await _cluster.WaitUntilAnswersAsync(
                () => _cluster.Store.ProbeAsync(coordination, cancellationToken),
                cancellationToken
            );
        }

        var host = _cluster.BuildNodeHost(Name, number, jobs, coordination, redis);
        await host.StartAsync(cancellationToken).ConfigureAwait(false);

        var incarnation = host.Services.GetRequiredService<ClusterIncarnation>();
        var running = new Running(host, incarnation, jobs, coordination);
        incarnation.Membership.LocalMembershipLostToken.Register(() =>
            running.MembershipLostAt ??= _cluster.Ledger.Now
        );
        _current = running;

        // The startup gate registers the node before the host finishes starting; a node without an owner claims
        // nothing, so the scenario would wait on it for no reason.
        if (incarnation.Membership.Identity is null)
        {
            throw new InvalidOperationException($"{Runner} started without a coordination identity.");
        }

        _cluster.Output($"{_cluster.Ledger.Now.TotalSeconds:F1}s started {Runner} as {OwnerTag}");
    }

    /// <summary>
    /// Crashes the incarnation: from this instant its bodies record nothing, both routes are cut so no write reaches
    /// the stores, and the host is stopped on a cancelled token and disposed, the way a process that died leaves.
    /// </summary>
    public async Task KillAsync()
    {
        var current = _Take();
        _cluster.Ledger.MarkKilled(current.Incarnation.Runner);
        await _routes.Jobs.DisableAsync().ConfigureAwait(false);
        await CoordinationRoute.DisableAsync().ConfigureAwait(false);
        _cluster.Output($"{_cluster.Ledger.Now.TotalSeconds:F1}s killed {current.Incarnation.Runner}");

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync().ConfigureAwait(false);
        await _StopAsync(current, cancelled.Token).ConfigureAwait(false);
    }

    /// <summary>Stops the incarnation the way a host shutdown does: Jobs drains within its budget, then hands back.</summary>
    public async Task StopAsync()
    {
        if (_current is null)
        {
            return;
        }

        var current = _Take();
        await _StopAsync(current, CancellationToken.None).ConfigureAwait(false);
        _cluster.Output($"{_cluster.Ledger.Now.TotalSeconds:F1}s stopped {current.Incarnation.Runner}");
    }

    /// <summary>Holds both routes open while dropping every reply for <paramref name="length"/>, then lets them through.</summary>
    public async Task FreezeAsync(TimeSpan length, CancellationToken cancellationToken)
    {
        var blackhole = new Dictionary<string, object>(StringComparer.Ordinal) { ["timeout"] = 0 };
        await _routes.Jobs.AddToxicAsync(_FreezeToxic, "timeout", blackhole, cancellationToken).ConfigureAwait(false);
        await CoordinationRoute
            .AddToxicAsync(_FreezeToxic, "timeout", blackhole, cancellationToken)
            .ConfigureAwait(false);

        // real-wait: the freeze is a real outage the nodes must ride out on their own clocks
        await Task.Delay(length, cancellationToken).ConfigureAwait(false);

        // Removing a single toxic can hang Toxiproxy 2.12; a reset of this slot's own server clears both.
        await _routes.Server.ResetAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Cuts the coordination route, or both routes, for <paramref name="length"/>, then restores them.</summary>
    public async Task PartitionAsync(bool coordinationOnly, TimeSpan length, CancellationToken cancellationToken)
    {
        await CutAsync(coordinationOnly, cancellationToken).ConfigureAwait(false);

        // real-wait: the partition is a real outage the nodes must ride out on their own clocks
        await Task.Delay(length, cancellationToken).ConfigureAwait(false);

        await _routes.Server.ResetAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Cuts the coordination route, or both routes, until <see cref="RestoreRoutesAsync"/>.</summary>
    public async Task CutAsync(bool coordinationOnly, CancellationToken cancellationToken)
    {
        await CoordinationRoute.DisableAsync(cancellationToken).ConfigureAwait(false);

        if (!coordinationOnly)
        {
            await _routes.Jobs.DisableAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public Task RestoreRoutesAsync(CancellationToken cancellationToken) => _routes.Server.ResetAsync(cancellationToken);

    /// <summary>Replaces the incarnation when it crossed its self-fence, since a fenced node never claims again.</summary>
    public async Task ReplaceIfFencedAsync(CancellationToken cancellationToken)
    {
        if (!IsFenced)
        {
            return;
        }

        _cluster.Output($"{_cluster.Ledger.Now.TotalSeconds:F1}s {Runner} is fenced; replacing it");
        await StopAsync().ConfigureAwait(false);
        await StartAsync(cancellationToken).ConfigureAwait(false);
    }

    private Running _Take()
    {
        var current = _Current;
        _current = null;
        return current;
    }

    private async Task _StopAsync(Running current, CancellationToken cancellationToken)
    {
        try
        {
            await current
                .Host.StopAsync(cancellationToken)
                .WaitAsync(ClusterTimings.ShutdownTimeout + _AbandonTimeout, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // A stop over cut routes or past its budget is part of the fault, not a finding.
            _cluster.Output(
                $"stopping {current.Incarnation.Runner} ended with {exception.GetType().Name}: {exception.Message}"
            );
        }

        try
        {
            if (current.Host is IAsyncDisposable disposable)
            {
                await disposable.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                current.Host.Dispose();
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _cluster.Output(
                $"disposing {current.Incarnation.Runner} ended with {exception.GetType().Name}: {exception.Message}"
            );
        }

        _cluster.Store.ClearPool(current.JobsConnection);

        if (current.CoordinationConnection is { } coordination)
        {
            _cluster.Store.ClearPool(coordination);
        }
    }

    private sealed class Running(
        IHost host,
        ClusterIncarnation incarnation,
        string jobsConnection,
        string? coordinationConnection
    )
    {
        public IHost Host { get; } = host;

        public ClusterIncarnation Incarnation { get; } = incarnation;

        public string JobsConnection { get; } = jobsConnection;

        public string? CoordinationConnection { get; } = coordinationConnection;

        public TimeSpan? MembershipLostAt { get; set; }
    }
}

/// <summary>Where the cluster keeps coordination membership.</summary>
public enum ClusterCoordination
{
    /// <summary>In the same database as the Jobs store, through its own route.</summary>
    Database,

    /// <summary>In Redis, through its own route, while the Jobs store stays relational.</summary>
    Redis,
}

internal static class RedisProbe
{
    public static ConfigurationOptions Options(string endpoint, bool abortOnConnectFail)
    {
        var options = ConfigurationOptions.Parse(endpoint);
        options.AbortOnConnectFail = abortOnConnectFail;
        options.ConnectTimeout = 5000;
        options.SyncTimeout = 5000;
        options.AsyncTimeout = 5000;
        return options;
    }
}
