// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Context;
using Headless.Coordination;
using Headless.Hosting;
using Headless.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Tests.Cluster;

/// <summary>
/// One cluster run: a fresh database and cluster name, up to <see cref="ClusterStoreFixture.MaxNodes"/> real Jobs
/// hosts in this process, each behind its own Toxiproxy routes, and a control host that enqueues and reads the
/// liveness snapshot without ever joining the cluster.
/// </summary>
public sealed class JobsCluster : IAsyncDisposable
{
    private static readonly TimeSpan _RouteAnswerTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan _RouteRetryInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan _PollInterval = TimeSpan.FromSeconds(1);

    private readonly Action<string>? _output;
    private readonly List<ClusterNode> _nodes = [];
    private readonly HashSet<string> _watchedOwners = [with(StringComparer.Ordinal)];
    private readonly List<LeaseSample> _leaseSamples = [];
    private readonly Lock _sampleLock = new();
    private IHost? _control;
    private CancellationTokenSource? _sampling;
    private Task? _sampler;
    private int _enqueueFailures;
    private int _sampleFailures;

    private JobsCluster(
        ClusterStoreFixture store,
        string connectionString,
        ClusterCoordination coordination,
        Action<string>? output
    )
    {
        Store = store;
        ConnectionString = connectionString;
        Coordination = coordination;
        _output = output;
        Name = "cj" + Guid.NewGuid().ToString("N")[..10];
        Ledger = new JobLedger();
        Log = new ClusterLog(Ledger);
        Reader = new ClusterStoreReader(store, connectionString);
        Tracker = new OrphanTracker();
    }

    /// <summary>The coordination cluster name, unique per run so no run meets another's nodes.</summary>
    public string Name { get; }

    public ClusterStoreFixture Store { get; }

    /// <summary>The run's database, reached directly.</summary>
    public string ConnectionString { get; }

    public ClusterCoordination Coordination { get; }

    public JobLedger Ledger { get; }

    public ClusterLog Log { get; }

    public ClusterStoreReader Reader { get; }

    public OrphanTracker Tracker { get; }

    public IReadOnlyList<ClusterNode> Nodes => _nodes;

    public int CountEnqueueFailures() => Volatile.Read(ref _enqueueFailures);

    public int CountSampleFailures() => Volatile.Read(ref _sampleFailures);

    private IHost Control => _control ?? throw new InvalidOperationException("The cluster has not started.");

    /// <summary>Creates the run's database and schema and the control host; no node starts yet.</summary>
    public static async Task<JobsCluster> CreateAsync(
        ClusterStoreFixture store,
        ClusterCoordination coordination,
        int nodes,
        Action<string>? output,
        CancellationToken cancellationToken
    )
    {
        var connectionString = await store.CreateDatabaseAsync(cancellationToken).ConfigureAwait(false);
        var cluster = new JobsCluster(store, connectionString, coordination, output);

        for (var slot = 0; slot < nodes; slot++)
        {
            cluster._nodes.Add(new ClusterNode(cluster, slot, store.Routes[slot]));
        }

        cluster._control = cluster._BuildControlHost();
        await JobsCoordinationFixtureExtensions
            .CreateJobsSchemaAsync(cluster._control, cancellationToken)
            .ConfigureAwait(false);
        output?.Invoke($"cluster {cluster.Name} on {store.Store} ({coordination} coordination), {nodes} node(s)");

        return cluster;
    }

    /// <summary>Starts every node that is not running.</summary>
    public async Task StartNodesAsync(CancellationToken cancellationToken)
    {
        foreach (var node in _nodes.Where(node => !node.IsRunning))
        {
            await node.StartAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Enqueues one job through the control host and records it; null when the store refused it.</summary>
    public async Task<JobSpec?> EnqueueAsync(WorkItem item, CancellationToken cancellationToken, bool doubleRun = false)
    {
        try
        {
            var id = await Control
                .Services.GetRequiredService<IJobScheduler>()
                .EnqueueAsync(item.Request(doubleRun), item.Options, cancellationToken)
                .ConfigureAwait(false);
            var spec = item.Spec(id);
            Ledger.RecordEnqueued(spec);
            return spec;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The control host reaches the store directly, so a refusal is unexpected; it is counted and reported.
            Interlocked.Increment(ref _enqueueFailures);
            Output($"enqueue #{item.Index} failed: {exception.GetType().Name}: {exception.Message}");
            return null;
        }
    }

    /// <summary>Enqueues <paramref name="perSecond"/> jobs a second for <paramref name="duration"/>.</summary>
    /// <returns>How many jobs were enqueued.</returns>
    public async Task<int> RunWorkloadAsync(
        ClusterWorkload workload,
        double perSecond,
        TimeSpan duration,
        CancellationToken cancellationToken
    )
    {
        const int ticksPerSecond = 4;
        var started = Ledger.Now;
        var sent = 0;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1.0 / ticksPerSecond));

        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            var elapsed = Ledger.Now - started;
            var target = (int)(Math.Min(elapsed.TotalSeconds, duration.TotalSeconds) * perSecond);

            // Catches up on every tick, so a slow enqueue does not lower the total.
            while (sent < target)
            {
                sent++;
                await EnqueueAsync(workload.Next(), cancellationToken).ConfigureAwait(false);
            }

            if (elapsed >= duration)
            {
                return sent;
            }
        }

        return sent;
    }

    /// <summary>Runs <paramref name="plan"/>, each fault at its offset from <paramref name="origin"/> on the ledger clock.</summary>
    public async Task<int> RunFaultPlanAsync(
        ClusterFaultPlan plan,
        TimeSpan origin,
        CancellationToken cancellationToken
    )
    {
        var injected = 0;

        foreach (var fault in plan.Faults)
        {
            var wait = origin + fault.At - Ledger.Now;

            if (wait > TimeSpan.Zero)
            {
                // real-wait: faults fire at their planned offsets while the workload runs on real time
                await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
            }

            await InjectAsync(fault, cancellationToken).ConfigureAwait(false);
            injected++;
        }

        return injected;
    }

    /// <summary>Injects one fault and leaves its node running a healthy incarnation afterwards.</summary>
    public async Task InjectAsync(ClusterFault fault, CancellationToken cancellationToken)
    {
        var node = _nodes[fault.Node];

        if (!node.IsRunning)
        {
            await node.StartAsync(cancellationToken).ConfigureAwait(false);
        }

        Output($"{Ledger.Now.TotalSeconds:F1}s inject {fault} on {node.Runner}");

        switch (fault.Kind)
        {
            case ClusterFaultKind.Kill:
                await node.KillAsync().ConfigureAwait(false);
                // real-wait: the slot stays empty for the fault's length, as a crashed process does until restarted
                await Task.Delay(fault.Length, cancellationToken).ConfigureAwait(false);
                await node.StartAsync(cancellationToken).ConfigureAwait(false);
                break;
            case ClusterFaultKind.Freeze:
                await node.FreezeAsync(fault.Length, cancellationToken).ConfigureAwait(false);
                await node.ReplaceIfFencedAsync(cancellationToken).ConfigureAwait(false);
                break;
            case ClusterFaultKind.CoordinationPartition:
            case ClusterFaultKind.FullPartition:
                await node.PartitionAsync(
                        fault.Kind == ClusterFaultKind.CoordinationPartition,
                        fault.Length,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
                await node.ReplaceIfFencedAsync(cancellationToken).ConfigureAwait(false);
                break;
            case ClusterFaultKind.GracefulRestart:
                await node.StopAsync().ConfigureAwait(false);
                await node.StartAsync(cancellationToken).ConfigureAwait(false);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(fault), fault.Kind, "Unknown fault kind.");
        }
    }

    /// <summary>Records lease reads of every row <paramref name="owner"/> holds, for the fence oracle.</summary>
    public void WatchLeasesOf(string owner)
    {
        lock (_sampleLock)
        {
            _watchedOwners.Add(owner);
        }
    }

    public IReadOnlyList<LeaseSample> LeaseSamples()
    {
        lock (_sampleLock)
        {
            return [.. _leaseSamples];
        }
    }

    /// <summary>Starts the periodic orphan sampler.</summary>
    public void StartSampling()
    {
        _sampling = new CancellationTokenSource();
        _sampler = _SampleLoopAsync(_sampling.Token);
    }

    public async Task StopSamplingAsync()
    {
        if (_sampling is null || _sampler is null)
        {
            return;
        }

        await _sampling.CancelAsync().ConfigureAwait(false);
        await _sampler.ConfigureAwait(false);
        _sampling.Dispose();
        _sampling = null;
        _sampler = null;
    }

    /// <summary>
    /// Waits until every enqueued job's row is terminal and no recorded body still runs, so the oracle judges a settled
    /// cluster and sees every late completion.
    /// </summary>
    public async Task<bool> WaitForRecoveryAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = Ledger.Now + timeout;

        while (true)
        {
            var ledger = Ledger.Snapshot();
            var rows = (await Reader.ReadRowsAsync(cancellationToken).ConfigureAwait(false)).ToDictionary(row =>
                row.Id
            );
            var settled =
                ledger.Specs.Keys.All(id => rows.TryGetValue(id, out var row) && row.IsTerminal)
                && ledger.Attempts.All(attempt => attempt.StoppedAt is not null);

            if (settled)
            {
                return true;
            }

            if (Ledger.Now >= deadline)
            {
                return false;
            }

            // real-wait: recovery is the real cluster's sweeps and reclaims, polled until they settle every row
            await Task.Delay(_PollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Polls <paramref name="condition"/> until it holds or <paramref name="timeout"/> passes.</summary>
    public async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = Ledger.Now + timeout;

        while (!condition())
        {
            if (Ledger.Now >= deadline)
            {
                return false;
            }

            // real-wait: the condition is a real cluster's progress, polled on the ledger clock
            await Task.Delay(_PollInterval, cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    /// <summary>Judges the run: the ledger, the rows, and the orphan intervals against the documented contract.</summary>
    public async Task<ClusterJudgement> JudgeAsync(CancellationToken cancellationToken)
    {
        var ledger = Ledger.Snapshot();
        var rows = await Reader.ReadRowsAsync(cancellationToken).ConfigureAwait(false);
        var verdict = ClusterVerdict.Evaluate(ledger, rows, Tracker.Intervals(), ClusterTimings.OrphanBound);

        return new ClusterJudgement(verdict, ledger, rows);
    }

    /// <summary>Retries <paramref name="probe"/> until it succeeds, so a host never starts behind a route that is not ready.</summary>
    public async Task WaitUntilAnswersAsync(Func<Task> probe, CancellationToken cancellationToken)
    {
        var deadline = Ledger.Now + _RouteAnswerTimeout;

        while (true)
        {
            try
            {
                await probe().ConfigureAwait(false);
                return;
            }
            catch (Exception exception)
                when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                if (Ledger.Now >= deadline)
                {
                    throw new InvalidOperationException(
                        $"A route did not answer within {_RouteAnswerTimeout.TotalSeconds:F0}s.",
                        exception
                    );
                }
            }

            // real-wait: a container port forwarder can refuse the first connections through a fresh route
            await Task.Delay(_RouteRetryInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    public static async Task PingRedisAsync(string endpoint)
    {
        await using var connection = await ConnectionMultiplexer
            .ConnectAsync(RedisProbe.Options(endpoint, abortOnConnectFail: true))
            .ConfigureAwait(false);
        await connection.GetDatabase().PingAsync().ConfigureAwait(false);
    }

    public void Output(string line) => _output?.Invoke(line);

    /// <summary>Builds one node's host: real scheduler, renewal, fallback, and dead-node sweeps, behind its routes.</summary>
    internal IHost BuildNodeHost(
        string node,
        int number,
        string jobsConnection,
        string? coordinationConnection,
        string? redisEndpoint
    )
    {
        var runner = $"{node}#{number.ToString(CultureInfo.InvariantCulture)}";
        var builder = Host.CreateEmptyApplicationBuilder(
            new HostApplicationBuilderSettings { ApplicationName = $"{Name}-{runner}" }
        );
        var services = builder.Services;

        services.AddLogging(logging =>
        {
            logging.AddProvider(Log.ProviderFor(runner));
            logging.SetMinimumLevel(LogLevel.Warning);
        });
        services.Configure<HostOptions>(options => options.ShutdownTimeout = ClusterTimings.ShutdownTimeout);
        // Each node gets a clock of its own registration; all read the system clock, since the lease decisions under
        // test run on the store clock and coordination paces its beats on this one.
        services.AddSingleton(TimeProvider.System);
        services.AddHeadlessHostIdentity(options => options.HostName = node);
        _AddCoordination(services, coordinationConnection, redisEndpoint, heartbeat: true);
        services.AddHeadlessJobs(options =>
        {
            options.AddModule<ClusterJobsModule>();
            options.ConfigureScheduler(ClusterTimings.Configure);
            options.UseEntityFramework(ef =>
            {
                ef.UseJobsDbContext<JobsDbContext>(db => Store.UseStore(db, jobsConnection));
                Store.UseClaims(ef);
            });
        });
        services.AddSingleton(provider => new ClusterIncarnation(
            node,
            number,
            Ledger,
            Reader,
            provider.GetRequiredService<INodeMembership>(),
            provider.GetRequiredService<IHostApplicationLifetime>()
        ));
        // A host start only verifies the coordination tables, so each node provisions them first, as a deploy would.
        services.AddDeployStepsOnStartup();

        return builder.Build();
    }

    public async ValueTask DisposeAsync()
    {
        await StopSamplingAsync().ConfigureAwait(false);

        foreach (var node in _nodes)
        {
            await node.StopAsync().ConfigureAwait(false);
            await node.RestoreRoutesAsync(CancellationToken.None).ConfigureAwait(false);
        }

        _control?.Dispose();
    }

    private IHost _BuildControlHost()
    {
        var builder = Host.CreateEmptyApplicationBuilder(
            new HostApplicationBuilderSettings { ApplicationName = $"{Name}-control" }
        );
        var services = builder.Services;
        services.AddLogging();
        services.AddHeadlessHostIdentity(options => options.HostName = "control");
        // The control host never beats and never starts, so it never registers and never shows in the snapshot.
        _AddCoordination(services, ConnectionString, Store.RedisEndpoint, heartbeat: false);
        services.AddHeadlessJobs(options =>
        {
            options.DisableBackgroundServices();
            options.DisableStartupInitialization();
            options.AddModule<ClusterJobsModule>();
            options.ConfigureScheduler(ClusterTimings.Configure);
            options.UseEntityFramework(ef =>
            {
                ef.UseJobsDbContext<JobsDbContext>(db => Store.UseStore(db, ConnectionString));
                Store.UseClaims(ef);
            });
        });

        return builder.Build();
    }

    private void _AddCoordination(
        IServiceCollection services,
        string? coordinationConnection,
        string? redisEndpoint,
        bool heartbeat
    )
    {
        if (Coordination == ClusterCoordination.Redis)
        {
            var endpoint = redisEndpoint ?? throw new InvalidOperationException("A Redis cluster needs an endpoint.");
            // A factory, so the host's container disposes the multiplexer with the host and no reconnect loop of a
            // killed incarnation outlives it.
            services.AddSingleton<IConnectionMultiplexer>(_ =>
                ConnectionMultiplexer.Connect(RedisProbe.Options(endpoint, abortOnConnectFail: false))
            );
        }

        services.AddHeadlessCoordination(setup =>
        {
            if (Coordination == ClusterCoordination.Redis)
            {
                setup.UseRedis(static _ => { });
            }
            else
            {
                Store.UseCoordination(
                    setup,
                    coordinationConnection ?? throw new InvalidOperationException("No coordination connection.")
                );
            }

            if (!heartbeat)
            {
                setup.DisableMembershipHeartbeat();
            }

            setup.Configure(options => ClusterTimings.Configure(options, Name));
        });
    }

    private async Task _SampleLoopAsync(CancellationToken cancellationToken)
    {
        var membership = Control.Services.GetRequiredService<INodeMembership>();
        using var timer = new PeriodicTimer(ClusterTimings.SamplePeriod);

        try
        {
            do
            {
                try
                {
                    await _SampleAsync(membership, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // A missed sample widens the orphan bound by one period; it is counted and reported.
                    Interlocked.Increment(ref _sampleFailures);
                }
            } while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Sampling stopped.
        }
    }

    private async Task _SampleAsync(INodeMembership membership, CancellationToken cancellationToken)
    {
        // Rows first, then the snapshot: an incarnation registers before it claims, so any owner the rows name is
        // already in a snapshot read afterwards.
        var at = Ledger.Now;
        var rows = await Reader.ReadRowsAsync(cancellationToken).ConfigureAwait(false);
        var snapshot = await membership.GetLivenessSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var live = snapshot
            .Where(node => node.State is NodeLivenessState.Alive or NodeLivenessState.Suspected)
            .Select(node => NodeOwnerTag.Format(Name, node.Identity))
            .ToHashSet(StringComparer.Ordinal);
        var held = rows.Where(row => !row.IsTerminal && row.Lease.State == Headless.Fencing.RowLeaseState.Active);

        Tracker.Observe(
            at,
            held.Where(row => row.Lease.Holder is { } holder && !live.Contains(holder))
                .Select(row => new OrphanKey(row.Id, row.Lease.Holder!, row.Lease.Generation))
        );

        lock (_sampleLock)
        {
            foreach (var row in held.Where(row => row.Lease.Holder is { } holder && _watchedOwners.Contains(holder)))
            {
                _leaseSamples.Add(new LeaseSample(at, row.Id, row.Lease.Generation, row.Lease.ExpiresAt));
            }
        }
    }
}

/// <summary>The verdict of a run with the ledger and rows it was judged on.</summary>
public sealed record ClusterJudgement(ClusterVerdict Verdict, LedgerSnapshot Ledger, IReadOnlyList<StoreRow> Rows)
{
    public string Evidence(ClusterLog log) =>
        Verdict.Evidence(Ledger, Rows)
        + (
            log.Count == 0
                ? string.Empty
                : $"{Environment.NewLine}last node warnings:{Environment.NewLine}{log.Tail(40)}"
        );
}
