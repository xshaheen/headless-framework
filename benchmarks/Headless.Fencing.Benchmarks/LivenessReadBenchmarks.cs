// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using BenchmarkDotNet.Attributes;
using Headless.Coordination;
using Headless.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Headless.Fencing.Benchmarks;

/// <summary>
/// The membership reads every node runs on each heartbeat tick and that the jobs fallback, cron agreement, and dead-owner
/// recovery run on top, at 32 concurrent callers: the liveness snapshot, one node's state, and the live set. The cluster
/// holds 16 registered nodes, all alive for the whole run. One operation is one read; Op/s is calls per second across all
/// callers.
/// </summary>
public class LivenessReadBenchmarks
{
    private const int _Callers = 32;
    private const int _ReadsPerCaller = 64;
    private const int _Operations = _Callers * _ReadsPerCaller;
    private const int _Nodes = 16;

    private BenchmarkDatabase _database = null!;
    private ServiceProvider _services = null!;
    private IMembershipStore _store = null!;
    private NodeIdentity[] _identities = null!;

    [Params(FencingProvider.PostgreSql, FencingProvider.SqlServer)]
    public FencingProvider Provider { get; set; }

    [GlobalSetup]
    public async Task SetupAsync()
    {
        _database = await BenchmarkDatabase.CreateAsync(Provider);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessCoordination(setup =>
        {
            if (Provider == FencingProvider.PostgreSql)
            {
                setup.UsePostgreSql(_database.ConnectionString);
            }
            else
            {
                setup.UseSqlServer(_database.ConnectionString);
            }

            // Thresholds far beyond a run's length keep every node alive without heartbeats, so each read classifies
            // the same rows from first iteration to last.
            setup.Configure(options =>
            {
                options.ClusterName = "bench";
                options.HeartbeatInterval = TimeSpan.FromMinutes(1);
                options.SuspicionThreshold = TimeSpan.FromHours(1);
                options.DeadThreshold = TimeSpan.FromHours(2);
                options.DeadRetentionWindow = TimeSpan.FromHours(2);
            });
            setup.DisableMembershipHeartbeat();
        });

        // A host start only verifies the schema, so the benchmark creates it first, as a development host does.
        services.AddDeployStepsOnStartup(o => o.Seed = false);
        _services = services.BuildServiceProvider();

        foreach (var initializer in _services.GetServices<IHostedService>().OfType<IHostedLifecycleService>())
        {
            await initializer.StartingAsync(CancellationToken.None);
        }

        _store = _services.GetRequiredService<IMembershipStore>();
        _identities = new NodeIdentity[_Nodes];

        for (var i = 0; i < _Nodes; i++)
        {
            var nodeId = new NodeId(string.Create(CultureInfo.InvariantCulture, $"node-{i:D2}"));
            var identity = new NodeIdentity(nodeId, await _store.AllocateIncarnationAsync(nodeId));

            if (!await _store.UpsertDescriptorAsync(new NodeDescriptor { Identity = identity, Role = "worker" }))
            {
                throw new InvalidOperationException($"Node {identity} was not registered.");
            }

            _identities[i] = identity;
        }

        // Every variant must read the registered cluster, or the run would measure an empty result.
        await Snapshot();
        await NodeState();
        await LiveSet();
    }

    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        if (_services is not null)
        {
            await _services.DisposeAsync();
        }

        if (_database is not null)
        {
            await _database.DisposeAsync();
        }
    }

    [Benchmark(OperationsPerInvoke = _Operations, Description = "ReadLivenessAsync()")]
    public Task Snapshot()
    {
        return _RunCallersAsync(async _ => _ExpectAll((await _store.ReadLivenessAsync()).Count));
    }

    [Benchmark(OperationsPerInvoke = _Operations, Description = "ReadNodeLivenessAsync(identity)")]
    public Task NodeState()
    {
        return _RunCallersAsync(async caller =>
        {
            if (await _store.ReadNodeLivenessAsync(_identities[caller % _Nodes]) != NodeLivenessState.Alive)
            {
                throw new InvalidOperationException("Expected a live node.");
            }
        });
    }

    [Benchmark(OperationsPerInvoke = _Operations, Description = "ReadLiveNodesAsync()")]
    public Task LiveSet()
    {
        return _RunCallersAsync(async _ => _ExpectAll((await _store.ReadLiveNodesAsync()).Count));
    }

    private static Task _RunCallersAsync(Func<int, Task> read)
    {
        var callers = new Task[_Callers];

        for (var i = 0; i < _Callers; i++)
        {
            var caller = i;
            callers[i] = Task.Run(async () =>
            {
                for (var n = 0; n < _ReadsPerCaller; n++)
                {
                    await read(caller);
                }
            });
        }

        return Task.WhenAll(callers);
    }

    private static void _ExpectAll(int count)
    {
        if (count != _Nodes)
        {
            throw new InvalidOperationException(
                string.Create(CultureInfo.InvariantCulture, $"Expected {_Nodes} live nodes, read {count}.")
            );
        }
    }
}
