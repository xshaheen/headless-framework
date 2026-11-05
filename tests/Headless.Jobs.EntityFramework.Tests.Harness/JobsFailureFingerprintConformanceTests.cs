// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Jobs;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

/// <summary>
/// The failure fingerprint on a relational store: a failure or retry write stores the fingerprint the executor
/// carries, a failure the store writes itself stores the fixed fingerprint of its cause in the same conditional
/// update, only the winning write stores one when two writers race to end a run, and a requeue clears it.
/// </summary>
public abstract class JobsFailureFingerprintConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : class, IJobsCoordinationFixture
{
    private const string _Function = "fingerprint-job";
    private const string _Carried = "v1:0123456789abcdef";
    private const string _RetryCarried = "v1:fedcba9876543210";

    public virtual async Task failure_and_retry_writes_store_the_carried_fingerprint()
    {
        await _RunAsync(
            "fingerprint-carried",
            async (store, owner, ct) =>
            {
                var job = Guid.NewGuid();
                await fixture.SeedTimeJobAsync(job, _Function, (int)JobStatus.InProgress, owner, ct);
                var occurrence = await _SeedRunningOccurrenceAsync(owner, NodeDeathPolicy.Retry, lapsed: false, ct);

                var retry = _State(job, owner)
                    .SetProperty(x => x.RetryCount, 1)
                    .SetProperty(x => x.FailureFingerprint, _RetryCarried);
                (await store.UpdateTimeJobAsync(retry, ct)).Should().Be(1);
                (await _FingerprintAsync(fixture.QualifiedTimeJobsTable, job, ct)).Should().Be(_RetryCarried);

                var failure = _State(job, owner)
                    .SetProperty(x => x.Status, JobStatus.Failed)
                    .SetProperty(x => x.ExceptionDetails, "boom")
                    .SetProperty(x => x.FailureFingerprint, _Carried);
                (await store.UpdateTimeJobAsync(failure, ct)).Should().Be(1);
                (await _FingerprintAsync(fixture.QualifiedTimeJobsTable, job, ct)).Should().Be(_Carried);

                var occurrenceFailure = _State(occurrence, owner)
                    .SetProperty(x => x.Status, JobStatus.Failed)
                    .SetProperty(x => x.ExceptionDetails, "boom")
                    .SetProperty(x => x.FailureFingerprint, _Carried);
                (await store.UpdateCronJobOccurrenceAsync(occurrenceFailure, ct)).Should().Be(1);
                (await _FingerprintAsync(fixture.QualifiedCronJobOccurrencesTable, occurrence, ct))
                    .Should()
                    .Be(_Carried);
            }
        );
    }

    public virtual async Task lease_lapsed_failure_stores_the_lease_lapsed_fingerprint()
    {
        await _RunAsync(
            "fingerprint-lease",
            async (store, owner, ct) =>
            {
                var job = Guid.NewGuid();
                await _SeedRunningTimeJobAsync(job, owner, lapsed: true, ct);
                var occurrence = await _SeedRunningOccurrenceAsync(owner, NodeDeathPolicy.MarkFailed, lapsed: true, ct);

                (await store.ReclaimStalledTimeJobsAsync(ct)).Should().Be(1);
                (await store.ReclaimStalledCronJobOccurrencesAsync(ct)).Should().Be(1);

                (await _FingerprintAsync(fixture.QualifiedTimeJobsTable, job, ct))
                    .Should()
                    .Be(JobFailureFingerprints.LeaseLapsed);
                (await _FingerprintAsync(fixture.QualifiedCronJobOccurrencesTable, occurrence, ct))
                    .Should()
                    .Be(JobFailureFingerprints.LeaseLapsed);
            }
        );
    }

    public virtual async Task dead_node_failure_stores_the_node_not_alive_fingerprint()
    {
        await _RunAsync(
            "fingerprint-dead-node",
            async (store, _, ct) =>
            {
                const string dead = "fingerprint-dead@1";
                var job = Guid.NewGuid();
                await _SeedRunningTimeJobAsync(job, dead, lapsed: true, ct);
                var occurrence = await _SeedRunningOccurrenceAsync(dead, NodeDeathPolicy.MarkFailed, lapsed: true, ct);

                (await store.ReleaseDeadNodeTimeJobResourcesAsync(dead, ct)).Should().Be(1);
                (await store.ReleaseDeadNodeOccurrenceResourcesAsync(dead, ct)).Should().Be(1);

                (await _FingerprintAsync(fixture.QualifiedTimeJobsTable, job, ct))
                    .Should()
                    .Be(JobFailureFingerprints.NodeNotAlive);
                (await _FingerprintAsync(fixture.QualifiedCronJobOccurrencesTable, occurrence, ct))
                    .Should()
                    .Be(JobFailureFingerprints.NodeNotAlive);
            }
        );
    }

    public virtual async Task interrupted_failure_stores_the_shutdown_fingerprint()
    {
        await _RunAsync(
            "fingerprint-shutdown",
            async (store, owner, ct) =>
            {
                var job = Guid.NewGuid();
                await _SeedRunningTimeJobAsync(job, owner, lapsed: false, ct);
                var occurrence = await _SeedRunningOccurrenceAsync(
                    owner,
                    NodeDeathPolicy.MarkFailed,
                    lapsed: false,
                    ct
                );

                (await store.SettleInterruptedTimeJobAsync(job, owner, "host stopped", null, ct)).Should().Be(1);
                (await store.SettleInterruptedCronJobOccurrenceAsync(occurrence, owner, "host stopped", ct))
                    .Should()
                    .Be(1);

                (await _FingerprintAsync(fixture.QualifiedTimeJobsTable, job, ct))
                    .Should()
                    .Be(JobFailureFingerprints.InterruptedByShutdown);
                (await _FingerprintAsync(fixture.QualifiedCronJobOccurrencesTable, occurrence, ct))
                    .Should()
                    .Be(JobFailureFingerprints.InterruptedByShutdown);
            }
        );
    }

    public virtual async Task only_the_winning_write_stores_a_fingerprint_when_two_writers_race_to_fail_a_run()
    {
        await _RunAsync(
            "fingerprint-race",
            async (store, owner, ct) =>
            {
                // Repeated so both orders of the race are exercised; each round asserts whichever order happened.
                for (var round = 0; round < 10; round++)
                {
                    var job = Guid.NewGuid();
                    await _SeedRunningTimeJobAsync(job, owner, lapsed: true, ct);
                    var failure = _State(job, owner)
                        .SetProperty(x => x.Status, JobStatus.Failed)
                        .SetProperty(x => x.ExceptionDetails, "boom")
                        .SetProperty(x => x.FailureFingerprint, _Carried);

                    var executor = Task.Run(() => store.UpdateTimeJobAsync(failure, ct), ct);
                    var sweep = Task.Run(() => store.ReclaimStalledTimeJobsAsync(ct), ct);
                    var executorWrote = await executor;
                    var sweepWrote = await sweep;

                    (executorWrote + sweepWrote).Should().Be(1, "exactly one writer ends the run");
                    (await _FingerprintAsync(fixture.QualifiedTimeJobsTable, job, ct))
                        .Should()
                        .Be(executorWrote == 1 ? _Carried : JobFailureFingerprints.LeaseLapsed);
                }
            }
        );
    }

    public virtual async Task requeue_clears_the_fingerprint()
    {
        await _RunAsync(
            "fingerprint-requeue",
            async (store, owner, ct) =>
            {
                var job = Guid.NewGuid();
                await _SeedRunningTimeJobAsync(job, owner, lapsed: true, ct);
                (await store.ReclaimStalledTimeJobsAsync(ct)).Should().Be(1);
                (await _FingerprintAsync(fixture.QualifiedTimeJobsTable, job, ct))
                    .Should()
                    .Be(JobFailureFingerprints.LeaseLapsed);

                (await store.RequeueTimeJobAsync(job, traceParent: null, ct)).Should().Be(JobRequeueOutcome.Requeued);

                (await _FingerprintAsync(fixture.QualifiedTimeJobsTable, job, ct)).Should().BeNull();
            }
        );
    }

    #region Helpers

    private async Task _RunAsync(
        string nodeId,
        Func<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>, string, CancellationToken, Task> scenario
    )
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost(nodeId);
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            await scenario(_Store(host), _Owner(host), ct);
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    private Task _SeedRunningTimeJobAsync(Guid id, string owner, bool lapsed, CancellationToken cancellationToken)
    {
        // Wide margins, so database-clock drift between seed and sweep cannot flip a row between lapsed and live.
        return fixture.SeedTimeJobAsync(
            id,
            _Function,
            (int)JobStatus.InProgress,
            owner,
            cancellationToken,
            NodeDeathPolicy.MarkFailed,
            lapsed ? DateTime.UtcNow.AddMinutes(-5) : DateTime.UtcNow.AddMinutes(10)
        );
    }

    private async Task<Guid> _SeedRunningOccurrenceAsync(
        string owner,
        NodeDeathPolicy policy,
        bool lapsed,
        CancellationToken cancellationToken
    )
    {
        var cronId = Guid.NewGuid();
        var occurrence = Guid.NewGuid();
        await fixture.SeedCronJobAsync(cronId, _Function, "* * * * *", policy, cancellationToken);
        await fixture.SeedCronOccurrenceAsync(
            occurrence,
            cronId,
            (int)JobStatus.InProgress,
            owner,
            policy,
            lapsed ? DateTime.UtcNow.AddMinutes(-5) : DateTime.UtcNow.AddMinutes(10),
            DateTime.UtcNow.AddMinutes(-1),
            cancellationToken
        );

        return occurrence;
    }

    private static JobExecutionState _State(Guid id, string owner)
    {
        return new JobExecutionState
        {
            FunctionName = _Function,
            JobId = id,
            ClaimOwner = owner,
        };
    }

    private async Task<string?> _FingerprintAsync(string table, Guid id, CancellationToken cancellationToken)
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = fixture.Sql($"SELECT \"FailureFingerprint\" FROM {table} WHERE \"Id\" = @id;");
        JobsCoordinationFixtureExtensions.AddParameter(command, "@id", id);

        var value = await command.ExecuteScalarAsync(cancellationToken);

        return value switch
        {
            null => throw new InvalidOperationException($"Row {id} not found in {table}."),
            DBNull => null,
            _ => (string)value,
        };
    }

    private static string _Owner(IHost host) => host.Services.GetRequiredService<INodeMembership>().GetOwnerTag()!;

    private static IJobPersistenceProvider<TimeJobEntity, CronJobEntity> _Store(IHost host) =>
        host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();

    #endregion
}
