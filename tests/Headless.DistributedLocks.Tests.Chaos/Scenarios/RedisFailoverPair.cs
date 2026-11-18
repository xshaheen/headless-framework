// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Networks;
using Headless.Testing.Testcontainers;
using StackExchange.Redis;
using Testcontainers.Redis;
using Tests.Oracle;
using Tests.Toxiproxy;

namespace Tests.Scenarios;

/// <summary>
/// A Redis primary and an asynchronous replica on one Docker network, with clients reaching the primary only through a
/// Toxiproxy route. <see cref="FailoverAsync"/> promotes the replica, moves the route to it, and demotes the old
/// primary to replicate from it, the way a managed failover moves a client's endpoint. Writes the replica had not
/// received when it was promoted are lost, which is the failover the distributed-locks guide calls best-effort fencing.
/// </summary>
public sealed class RedisFailoverPair : IAsyncDisposable
{
    private const int _RedisPort = 6379;
    private static readonly string[] _Aliases = ["redis-a", "redis-b"];
    private static readonly TimeSpan _SettleTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan _SettlePoll = TimeSpan.FromMilliseconds(100);

    private readonly INetwork _network = new NetworkBuilder().Build();
    private readonly RedisContainer[] _nodes;
    private ToxiproxyDatabaseRoute? _route;
    private ConnectionMultiplexer? _admin;
    private int _primary;

    private RedisFailoverPair()
    {
        _nodes =
        [
            .. _Aliases.Select(alias =>
                new RedisBuilder(TestImages.Redis)
                    .WithNetwork(_network)
                    .WithNetworkAliases(alias)
                    .WithHostPingWaitStrategy()
                    .Build()
            ),
        ];
    }

    /// <summary>How many failovers completed.</summary>
    public int Failovers { get; private set; }

    private ToxiproxyProxy Route => _route?.Proxy ?? throw new InvalidOperationException("The pair has not started.");

    /// <summary>A client configuration through the route that rides out a failover instead of failing its connect.</summary>
    public string ConnectionString(bool admin = false) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{Route.Host}:{Route.Port},abortConnect=false,connectTimeout=2000,syncTimeout=2000,asyncTimeout=2000,connectRetry=5{(admin ? ",allowAdmin=true" : string.Empty)}"
        );

    public static async Task<RedisFailoverPair> StartAsync(CancellationToken cancellationToken)
    {
        var pair = new RedisFailoverPair();

        try
        {
            await pair._network.CreateAsync(cancellationToken).ConfigureAwait(false);
            await Task.WhenAll(pair._nodes.Select(node => node.StartAsync(cancellationToken))).ConfigureAwait(false);
            await pair._CliAsync(1, ["REPLICAOF", _Aliases[0], "6379"], cancellationToken).ConfigureAwait(false);
            await pair._WaitReplicaLinkUpAsync(1, cancellationToken).ConfigureAwait(false);

            pair._route = await ToxiproxyDatabaseRoute
                .StartAsync(pair._nodes[0].GetMappedPublicPort(_RedisPort), cancellationToken)
                .ConfigureAwait(false);
            pair._admin = await ConnectionMultiplexer
                .ConnectAsync(pair.ConnectionString(admin: true))
                .ConfigureAwait(false);

            // Prove the route reaches the primary before any client starts on it.
            await pair._WaitPrimaryThroughRouteAsync(cancellationToken).ConfigureAwait(false);

            return pair;
        }
        catch
        {
            await pair.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Promotes the replica, moves the route to it while clients may be writing, demotes the old primary, and returns
    /// once the route answers from the new primary and the old one replicates from it again. The failover's span goes to
    /// <paramref name="ledger"/>, the only window in which the documented contract lets Redis reissue a token.
    /// </summary>
    public async Task FailoverAsync(FencingLedger ledger, CancellationToken cancellationToken)
    {
        var old = _primary;
        var next = 1 - old;
        ledger.FailoverStarted();

        await _CliAsync(next, ["REPLICAOF", "NO", "ONE"], cancellationToken).ConfigureAwait(false);
        await Route.DisableAsync(cancellationToken).ConfigureAwait(false);
        await Route.RepointAsync(_nodes[next].GetMappedPublicPort(_RedisPort), cancellationToken).ConfigureAwait(false);
        await Route.EnableAsync(cancellationToken).ConfigureAwait(false);
        await _CliAsync(old, ["REPLICAOF", _Aliases[next], "6379"], cancellationToken).ConfigureAwait(false);
        _primary = next;

        await _WaitPrimaryThroughRouteAsync(cancellationToken).ConfigureAwait(false);
        ledger.FailoverEnded();
        await _WaitReplicaLinkUpAsync(old, cancellationToken).ConfigureAwait(false);
        Failovers++;
    }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"failovers {Failovers}, primary {_Aliases[_primary]}");

    public async ValueTask DisposeAsync()
    {
        if (_admin is not null)
        {
            await _admin.DisposeAsync().ConfigureAwait(false);
        }

        if (_route is not null)
        {
            await _route.DisposeAsync().ConfigureAwait(false);
        }

        foreach (var node in _nodes)
        {
            await node.DisposeAsync().ConfigureAwait(false);
        }

        await _network.DisposeAsync().ConfigureAwait(false);
    }

    private async Task _CliAsync(int node, string[] arguments, CancellationToken cancellationToken)
    {
        var result = await _nodes[node].ExecAsync(["redis-cli", .. arguments], cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0 || !result.Stdout.Contains("OK", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"redis-cli {string.Join(' ', arguments)} on {_Aliases[node]} failed ({result.ExitCode}): {result.Stdout}{result.Stderr}"
            );
        }
    }

    // A route left on a replica answers writes with READONLY: a harness fault, not a lock finding, so wait it out here.
    private Task _WaitPrimaryThroughRouteAsync(CancellationToken cancellationToken) =>
        _SettleAsync(
            "the route to answer from a primary",
            async () =>
            {
                var admin = _admin ?? throw new InvalidOperationException("The pair has not started.");
                var role = (RedisResult[])(await admin.GetDatabase().ExecuteAsync("ROLE").ConfigureAwait(false))!;

                return string.Equals(role[0].ToString(), "master", StringComparison.Ordinal);
            },
            cancellationToken
        );

    private Task _WaitReplicaLinkUpAsync(int node, CancellationToken cancellationToken) =>
        _SettleAsync(
            $"{_Aliases[node]} to replicate",
            async () =>
            {
                var info = await _nodes[node]
                    .ExecAsync(["redis-cli", "INFO", "replication"], cancellationToken)
                    .ConfigureAwait(false);

                return info.Stdout.Contains("master_link_status:up", StringComparison.Ordinal);
            },
            cancellationToken
        );

    private static async Task _SettleAsync(string what, Func<Task<bool>> condition, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_SettleTimeout);
        Exception? last = null;

        while (true)
        {
            try
            {
                if (await condition().ConfigureAwait(false))
                {
                    return;
                }
            }
            catch (RedisException e)
            {
                last = e;
            }

            try
            {
                // real-wait: Redis replication and a client reconnect settle in real time; the condition is polled.
                await Task.Delay(_SettlePoll, deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException e) when (!cancellationToken.IsCancellationRequested)
            {
                throw new InvalidOperationException(
                    $"Waited {_SettleTimeout.TotalSeconds:0}s for {what}; last error: {last?.Message ?? "none"}",
                    e
                );
            }
        }
    }
}
