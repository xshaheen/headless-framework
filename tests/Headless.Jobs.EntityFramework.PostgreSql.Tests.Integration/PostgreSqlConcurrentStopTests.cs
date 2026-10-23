// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Coordination;
using Headless.Jobs;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

/// <summary>
/// A host that stops its services concurrently stops the coordination heartbeat alongside the Jobs scheduler. The
/// node must stay a live member until the scheduler has drained, so a peer never treats it as dead while a job it
/// still runs is settling its row.
/// </summary>
[Collection<PostgreSqlJobsCoordinationFixture>]
public sealed class PostgreSqlConcurrentStopTests(PostgreSqlJobsCoordinationFixture fixture) : TestBase
{
    private static readonly TimeSpan _WaitBudget = TimeSpan.FromSeconds(60);

    // How long the job keeps running after the stop begins: several of the peer's 500 ms beats, so a node that left
    // at the start of the stop is observed gone well before the job is released.
    private static readonly TimeSpan _DrainWindow = TimeSpan.FromSeconds(3);

    [Fact]
    public async Task concurrent_stop_keeps_the_node_a_member_until_a_running_job_persists_its_outcome()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        var id = Guid.NewGuid();

        using var node = _BuildHost("node-a", stopConcurrently: true);
        using var peer = _BuildPeer();
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(node, ct);
        await node.StartAsync(ct);
        var nodeIdentity = node.Services.GetRequiredService<INodeMembership>().Identity!.Value;
        await using var peerEvents = peer
            .Services.GetRequiredService<IMembershipEventSource>()
            .WatchAsync(ct)
            .GetAsyncEnumerator(ct);
        await peer.StartAsync(ct);

        try
        {
            await _NextEventAsync(peerEvents, e => e is NodeJoined joined && joined.Identity == nodeIdentity);
            await _EnqueueAsync(node, id, ReleasableJobsModule.FunctionName, ct);
            await ReleasableJobsModule.Started(id).WaitAsync(_WaitBudget, ct);

            (await _IsAliveAsync(peer, nodeIdentity, ct)).Should().BeTrue(await _DescribeAsync(peer, ct));

            // when: the host stops while the job is still running
            var stop = node.StopAsync(ct);
            var drainUntil = TimeProvider.System.GetTimestamp();
            while (TimeProvider.System.GetElapsedTime(drainUntil) < _DrainWindow)
            {
                (await _IsAliveAsync(peer, nodeIdentity, ct))
                    .Should()
                    .BeTrue(
                        "the node must not leave membership while its scheduler is still draining; "
                            + await _DescribeAsync(peer, ct)
                            + "; stop "
                            + stop.Status
                    );
                await Task.Delay(TimeSpan.FromMilliseconds(100), ct);
            }

            ReleasableJobsModule.Release(id);
            await stop.WaitAsync(_WaitBudget, ct);

            // then: the outcome persisted, the node left only afterwards, and the peer's reclaim found nothing
            await _NextEventAsync(peerEvents, e => e is NodeLeft left && left.Identity == nodeIdentity);
            var stored = await _ReadAsync(peer, id, ct);
            stored.Status.Should().BeOneOf(JobStatus.Succeeded, JobStatus.DueDone);
            stored.RetryCount.Should().Be(0);
            ReleasableJobsModule.Invocations(id).Should().Be(1);
        }
        finally
        {
            ReleasableJobsModule.Release(id);
            await peer.StopAsync(ct);
        }
    }

    [Fact]
    public async Task concurrent_stop_hands_back_a_zero_retry_job_that_outlasts_the_stop_budget_uncharged()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        var id = Guid.NewGuid();

        using var node = _BuildHost("node-a", stopConcurrently: true);
        using var peer = _BuildPeer();
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(node, ct);
        await node.StartAsync(ct);
        var nodeIdentity = node.Services.GetRequiredService<INodeMembership>().Identity!.Value;
        await using var peerEvents = peer
            .Services.GetRequiredService<IMembershipEventSource>()
            .WatchAsync(ct)
            .GetAsyncEnumerator(ct);
        await peer.StartAsync(ct);

        try
        {
            await _NextEventAsync(peerEvents, e => e is NodeJoined joined && joined.Identity == nodeIdentity);
            await _EnqueueAsync(node, id, GracefulStopJobsModule.FunctionName, ct);
            await GracefulStopJobsModule.FirstRunStarted(id).WaitAsync(_WaitBudget, ct);

            // when: the job ignores the stop until the scheduler cancels it at the release window
            var stop = node.StopAsync(ct);
            while (!stop.IsCompleted)
            {
                var row = await _ReadAsync(peer, id, ct);
                if (row.Status == JobStatus.InProgress && row.OwnerId == nodeIdentity.ToString())
                {
                    (await _IsAliveAsync(peer, nodeIdentity, ct))
                        .Should()
                        .BeTrue("the node must stay a member until its interrupted job is handed back");
                }

                await Task.Delay(TimeSpan.FromMilliseconds(100), ct);
            }

            await stop;

            // then: handed back uncharged, and still so once the peer has seen the node leave and run its reclaim
            await _NextEventAsync(peerEvents, e => e is NodeLeft left && left.Identity == nodeIdentity);
            var detail = await fixture.ReadTimeJobDetailAsync(id, ct);
            detail.Status.Should().Be((int)JobStatus.Idle);
            detail.OwnerId.Should().BeNull();
            (await fixture.ReadTimeJobRetryCountAsync(id, ct))
                .Should()
                .Be(0, "a graceful stop is not a used-up attempt and no peer reclaim charged it");
            GracefulStopJobsModule.Invocations(id).Should().Be(1);
        }
        finally
        {
            await peer.StopAsync(ct);
        }
    }

    // The peer runs no scheduler, so it never claims the job itself; its coordination heartbeat and dead-owner
    // reclaim still run, which is what would take the job from a node that left too early.
    private IHost _BuildPeer() => fixture.BuildHost("node-b");

    private IHost _BuildHost(string nodeId, bool stopConcurrently) =>
        fixture.BuildHost(
            nodeId,
            configureJobs: jobs =>
                jobs.AddModule<ReleasableJobsModule>()
                    .AddModule<GracefulStopJobsModule>()
                    .ConfigureScheduler(scheduler => scheduler.ShutdownTimeout = TimeSpan.FromSeconds(8)),
            runBackgroundServices: true,
            configureServices: services =>
                services.Configure<HostOptions>(options => options.ServicesStopConcurrently = stopConcurrently)
        );

    private static async Task _EnqueueAsync(IHost host, Guid id, string function, CancellationToken ct)
    {
        var store = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
        await store.AddTimeJobsAsync(
            [
                new TimeJobEntity
                {
                    Id = id,
                    Function = function,
                    ExecutionTime = DateTime.UtcNow,
                    Retries = 0,
                    OnNodeDeath = NodeDeathPolicy.Retry,
                },
            ],
            ct
        );
    }

    private static async Task<TimeJobEntity> _ReadAsync(IHost host, Guid id, CancellationToken ct)
    {
        var store = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();

        return (await store.GetTimeJobByIdAsync(id, ct))!;
    }

    private static async Task<bool> _IsAliveAsync(IHost observer, NodeIdentity identity, CancellationToken ct)
    {
        var snapshots = await observer.Services.GetRequiredService<INodeMembership>().GetLivenessSnapshotAsync(ct);

        return snapshots.Any(s => s.Identity == identity && s.State == NodeLivenessState.Alive);
    }

    private static async Task<string> _DescribeAsync(IHost observer, CancellationToken ct)
    {
        var snapshots = await observer.Services.GetRequiredService<INodeMembership>().GetLivenessSnapshotAsync(ct);

        return string.Join(", ", snapshots.Select(s => $"{s.Identity}={s.State}"));
    }

    private static async Task _NextEventAsync(
        IAsyncEnumerator<NodeMembershipEvent> events,
        Func<NodeMembershipEvent, bool> match
    )
    {
        while (await events.MoveNextAsync().AsTask().WaitAsync(_WaitBudget, AbortToken))
        {
            if (match(events.Current))
            {
                return;
            }
        }

        throw new InvalidOperationException("The membership event stream ended before the expected event.");
    }
}

/// <summary>
/// One time-job function whose run waits until the test releases it, then succeeds. State is keyed by job id, so
/// parallel scenarios never share it.
/// </summary>
public sealed class ReleasableJobsModule : IJobsModule
{
    public const string FunctionName = "Releasable_Sample";

    private static readonly ConcurrentDictionary<Guid, int> _Invocations = new();
    private static readonly ConcurrentDictionary<Guid, TaskCompletionSource> _Started = new();
    private static readonly ConcurrentDictionary<Guid, TaskCompletionSource> _Released = new();

    private ReleasableJobsModule() { }

    public static Task Started(Guid jobId) => _Signal(_Started, jobId).Task;

    public static void Release(Guid jobId) => _Signal(_Released, jobId).TrySetResult();

    public static int Invocations(Guid jobId) => _Invocations.GetValueOrDefault(jobId);

    private static TaskCompletionSource _Signal(ConcurrentDictionary<Guid, TaskCompletionSource> signals, Guid jobId) =>
        signals.GetOrAdd(jobId, static _ => new(TaskCreationOptions.RunContinuationsAsynchronously));

    private static async Task _RunAsync(Guid jobId, CancellationToken cancellationToken)
    {
        _Invocations.AddOrUpdate(jobId, 1, static (_, count) => count + 1);
        _Signal(_Started, jobId).TrySetResult();
        await _Signal(_Released, jobId).Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    static void IJobsModule.Register(JobsCatalogBuilder catalog)
    {
        catalog.AddFunctions(
            new Dictionary<string, JobFunctionRegistration>(StringComparer.Ordinal)
            {
                [FunctionName] = new JobFunctionRegistration
                {
                    CronExpression = string.Empty,
                    Priority = JobPriority.Normal,
                    Delegate = static (_, context, cancellationToken) => _RunAsync(context.Id, cancellationToken),
                    MaxConcurrency = 0,
                    JobType = typeof(ReleasableJob),
                },
            }
        );
        catalog.AddDescriptors(
            new Dictionary<string, JobFunctionDescriptor>(StringComparer.Ordinal)
            {
                [FunctionName] = new(FunctionName, null, string.Empty, JobPriority.Normal, 0),
            }
        );
    }

    public sealed class ReleasableJob : IJob
    {
        public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }
}
