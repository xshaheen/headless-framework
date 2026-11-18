// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Coordination;
using Headless.Jobs;
using Headless.Testing.Testcontainers;
using Microsoft.EntityFrameworkCore;
using Tests.Toxiproxy;

namespace Tests.Cluster;

/// <summary>
/// The containers one store leaf's cluster scenarios share: the database that holds both the Jobs store and the
/// relational coordination tables, a Redis server for the split-store scenario, and one Toxiproxy server per node slot.
/// Each server publishes the three routes one node needs (Jobs store, relational coordination, Redis coordination), so
/// resetting one node's faults never touches another node.
/// </summary>
/// <remarks>Each scenario runs in its own database and cluster name, so no run meets an earlier run's rows or nodes.</remarks>
public abstract class ClusterStoreFixture : IAsyncLifetime
{
    /// <summary>The most nodes a scenario runs.</summary>
    public const int MaxNodes = 3;

    private readonly RedisComponent _redis = new();
    private readonly List<ToxiproxyServer> _servers = [];
    private readonly List<NodeRoutes> _routes = [];
    private int _databases;

    /// <summary>The store's name in the report, such as <c>PostgreSQL</c>.</summary>
    public abstract string Store { get; }

    /// <summary>The routes of each node slot.</summary>
    public IReadOnlyList<NodeRoutes> Routes => _routes;

    public string RedisHost => _redis.Container.Hostname;

    public int RedisPort => _redis.Container.GetMappedPublicPort(6379);

    /// <summary>The Redis server reached directly, as <c>host:port</c>.</summary>
    public string RedisEndpoint => $"{RedisHost}:{RedisPort.ToString(CultureInfo.InvariantCulture)}";

    protected abstract IAsyncLifetime Database { get; }

    /// <summary>The database port the Docker host publishes, which the store routes forward to.</summary>
    protected abstract int DatabaseHostPort { get; }

    /// <summary>Creates a fresh database for one scenario run and returns its direct connection string.</summary>
    public async Task<string> CreateDatabaseAsync(CancellationToken cancellationToken)
    {
        var suffix = "c" + Interlocked.Increment(ref _databases).ToString(CultureInfo.InvariantCulture);
        return await CreateDatabaseAsync(suffix, cancellationToken).ConfigureAwait(false);
    }

    protected abstract Task<string> CreateDatabaseAsync(string suffix, CancellationToken cancellationToken);

    /// <summary>
    /// The connection string a node uses to reach <paramref name="direct"/> through <paramref name="route"/>. Each
    /// incarnation passes its own <paramref name="applicationName"/>, so it gets a pool of its own that no later
    /// incarnation can inherit.
    /// </summary>
    public abstract string Proxied(string direct, ToxiproxyProxy route, string applicationName);

    public abstract void UseStore(DbContextOptionsBuilder db, string connectionString);

    public abstract void UseClaims(JobsEfCoreOptionBuilder<TimeJobEntity, CronJobEntity> ef);

    public abstract void UseCoordination(HeadlessCoordinationSetupBuilder setup, string connectionString);

    public abstract DbConnection CreateConnection(string connectionString);

    /// <summary>Closes every idle pooled connection of <paramref name="connectionString"/>.</summary>
    public abstract void ClearPool(string connectionString);

    /// <summary>The harness's store view over one run's database, for its naming and seed helpers.</summary>
    public abstract IJobsCoordinationFixture View(string connectionString);

    /// <summary>Opens one connection and runs one statement, so a route a host is about to use is known to answer.</summary>
    public async Task ProbeAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";
        await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask InitializeAsync()
    {
        await Database.InitializeAsync();
        await ((IAsyncLifetime)_redis).InitializeAsync();

        for (var node = 0; node < MaxNodes; node++)
        {
            var server = new ToxiproxyServer();
            _servers.Add(server);
            await server.StartAsync();
            _routes.Add(
                new NodeRoutes(
                    server,
                    await server.CreateProxyAsync("jobs", DatabaseHostPort),
                    await server.CreateProxyAsync("coordination", DatabaseHostPort),
                    await server.CreateProxyAsync("redis", RedisPort)
                )
            );
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var server in _servers)
        {
            await server.DisposeAsync();
        }

        await ((IAsyncLifetime)_redis).DisposeAsync();
        await Database.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private sealed class RedisComponent : HeadlessRedisFixture;
}

/// <summary>The three routes of one node slot, all on the slot's own Toxiproxy server.</summary>
/// <param name="Server">The slot's server; resetting it clears every fault on this slot only.</param>
/// <param name="Jobs">The route to the Jobs store.</param>
/// <param name="Coordination">The route to the relational coordination store (the same database).</param>
/// <param name="Redis">The route to the Redis coordination store.</param>
public sealed record NodeRoutes(
    ToxiproxyServer Server,
    ToxiproxyProxy Jobs,
    ToxiproxyProxy Coordination,
    ToxiproxyProxy Redis
);
