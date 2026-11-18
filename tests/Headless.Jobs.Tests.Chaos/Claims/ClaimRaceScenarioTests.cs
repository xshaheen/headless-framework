// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Data.Common;
using System.Globalization;
using Headless.Jobs;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tests.Scheduling;

namespace Tests.Claims;

/// <summary>
/// S6, the claim race with mutation: sixteen Jobs nodes, each with its own owner identity, claim time jobs off one store
/// in a tight loop while two mutators call <c>ITimeJobManager.UpdateAsync</c> on the same rows with the entity they just
/// read. <see cref="ClaimHistoryVerdict"/> holds the oracle: no row runs twice, no generation has two holders, no edit
/// lands on a started row, and no edit leaves run-state the run did not write.
/// </summary>
public abstract class ClaimRaceScenarioTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : class, IStoreChaosFixture
{
    private const string _Scenario = "S6 claim race with mutation";
    private const int _Nodes = 16;
    private const int _Mutators = 2;
    private static readonly TimeSpan _Poll = TimeSpan.FromMilliseconds(250);

    public virtual async Task should_run_every_row_once_and_keep_its_run_state_when_sixteen_claimers_race_an_editor()
    {
        var ct = AbortToken;
        var settings = ChaosRunSettings.Current;
        var seed = settings.SeedFor(_Scenario);
        var rowCount = settings.Pick(400, 4000);
        var spread = settings.Pick(TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(60));
        var output = TestContext.Current.TestOutputHelper;
        output?.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"{_Scenario}: {settings}, scenario seed {seed}, {_Nodes} nodes, {rowCount} rows over {spread}"
            )
        );

        await fixture.ResetDatabaseAsync(ct);
        var ledger = new ClaimLedger();
        var hosts = Enumerable
            .Range(0, _Nodes)
            .Select(i =>
                fixture.BuildHost(
                    string.Create(CultureInfo.InvariantCulture, $"claim-race-{i}"),
                    runBackgroundServices: true,
                    configureJobs: jobs =>
                        jobs.AddModule<ClaimChaosJobsModule>()
                            // Every node sweeps for due rows each second, so all sixteen race every row, not only the
                            // node whose own wake planned it.
                            .ConfigureScheduler(scheduler =>
                                scheduler.FallbackIntervalChecker = TimeSpan.FromSeconds(1)
                            ),
                    configureServices: services =>
                    {
                        services.AddSingleton(ledger);
                        services.AddSingleton(
                            new ChaosNodeName(string.Create(CultureInfo.InvariantCulture, $"node-{i}"))
                        );
                    }
                )
            )
            .ToList();

        try
        {
            await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(hosts[0], ct);

            // Rows exist before any node starts, so every node's first poll plans a wake for them.
            var start = (await _StoreUtcNowAsync(ct)).AddSeconds(20);
            var timeJobs = hosts[0].Services.GetRequiredService<ITimeJobManager<TimeJobEntity>>();
            var rows = Enumerable
                .Range(0, rowCount)
                .Select(i => new TimeJobEntity
                {
                    Id = Guid.NewGuid(),
                    Function = ClaimChaosJobsModule.Function,
                    Description = "seeded",
                    ExecutionTime = start + spread * i / rowCount,
                    Retries = 0,
                })
                .ToList();

            foreach (var chunk in rows.Chunk(500))
            {
                await timeJobs.AddBatchAsync([.. chunk], ct);
            }

            var ids = rows.Select(x => x.Id).ToArray();

            foreach (var host in hosts)
            {
                await host.StartAsync(ct);
            }

            using var stopMutating = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var store = hosts[0].Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            var mutators = new Task[_Mutators];

            for (var m = 0; m < _Mutators; m++)
            {
#pragma warning disable AsyncFixer04 // False positive: every mutator is awaited below, before the token source is disposed.
                mutators[m] = _MutateAsync(store, timeJobs, ledger, ids, seed + m, stopMutating.Token);
#pragma warning restore AsyncFixer04
            }

            var unfinished = await _WaitForAllFinishedAsync(spread + TimeSpan.FromMinutes(2), ct);
            await stopMutating.CancelAsync();
            await Task.WhenAll(mutators);

            foreach (var host in hosts)
            {
                await host.StopAsync(ct);
            }

            var finals = await _ReadFinalsAsync(hosts[0], ids, ct);
            var verdict = ClaimHistoryVerdict.Evaluate(ids, ledger.Starts, ledger.Edits, ledger.Observations, finals);
            List<string> violations = [.. verdict.Violations()];

            if (unfinished > 0)
            {
                violations.Add(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{unfinished} row(s) were still unfinished at the deadline"
                    )
                );
            }

            ChaosReport.Record(
                fixture.Store,
                _Scenario,
                violations,
                verdict.Describe(),
                verdict.Evidence(),
                line => output?.WriteLine(line)
            );

            _AssertOracleCatchesSabotage(ids, ledger, finals);

            violations.Should().BeEmpty();
        }
        finally
        {
            foreach (var host in hosts)
            {
                host.Dispose();
            }
        }
    }

    // The oracle must catch a double run, a lost run, and an edit after a start on this run's own records.
    private static void _AssertOracleCatchesSabotage(
        Guid[] ids,
        ClaimLedger ledger,
        IReadOnlyList<ClaimFinalRow> finals
    )
    {
        var starts = ledger.Starts;
        var picked = starts.DistinctBy(x => x.JobId).Take(3).ToList();

        if (picked.Count < 3)
        {
            return;
        }

        var (twice, lost, edited) = (picked[0], picked[1], picked[2]);
        List<ClaimStart> sabotagedStarts =
        [
            .. starts.Where(x => x.JobId != lost.JobId),
            twice with
            {
                Node = "sabotage",
                Sequence = twice.Sequence + 1,
            },
        ];
        List<ClaimEdit> sabotagedEdits = [.. ledger.Edits, new ClaimEdit(edited.JobId, edited.Sequence + 1, true)];

        var verdict = ClaimHistoryVerdict.Evaluate(ids, sabotagedStarts, sabotagedEdits, ledger.Observations, finals);

        verdict.RunTwice.Should().Contain(x => x.StartsWith(_Short(twice.JobId), StringComparison.Ordinal));
        verdict.NeverRan.Should().Contain(_Short(lost.JobId));
        verdict.EditAfterStart.Should().Contain(x => x.StartsWith(_Short(edited.JobId), StringComparison.Ordinal));
    }

    private static string _Short(Guid id) => id.ToString("N", CultureInfo.InvariantCulture)[..8];

    // Reads a row near the claim frontier, records its lease, and sends it straight back with a new description: the stale entity an
    // operator's edit would carry, run-state columns included.
    private static async Task _MutateAsync(
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> store,
        ITimeJobManager<TimeJobEntity> manager,
        ClaimLedger ledger,
        Guid[] ids,
        int seed,
        CancellationToken ct
    )
    {
        var random = new Random(seed);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                // Edit near the claim frontier: the rows fall due in id order, so the rows just past the last start
                // are the ones nodes are claiming and starting right now.
                var frontier = ledger.StartCount;
                var id = ids[random.Next(Math.Max(0, frontier - 16), Math.Min(ids.Length, frontier + 48))];
                var row = await store.GetTimeJobByIdAsync(id, ct);

                if (row is null)
                {
                    continue;
                }

                ledger.Observe(new ClaimObservation(id, row.Lease.Generation, row.Lease.State, row.Lease.Holder));

                if (row.Status is not (JobStatus.Idle or JobStatus.Queued or JobStatus.InProgress))
                {
                    continue;
                }

                var sequence = ledger.Next();
                row.Description = string.Create(CultureInfo.InvariantCulture, $"edit-{sequence}");
                var result = await manager.UpdateAsync(row, ct);
                ledger.Edit(new ClaimEdit(id, sequence, result.IsSucceeded));
            }
        }
        catch (Exception) when (ct.IsCancellationRequested)
        {
            // The race is over. SqlClient reports a read cut short by the stop as an aborted batch rather than a
            // cancellation, so whatever the in-flight call threw after the stop is not a finding.
        }
    }

    private async Task<int> _WaitForAllFinishedAsync(TimeSpan deadline, CancellationToken ct)
    {
        var polls = (int)(deadline / _Poll);
        var unfinished = int.MaxValue;

        for (var poll = 0; poll < polls; poll++)
        {
            await using var connection = fixture.CreateConnection();
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = fixture.Sql(
                $"SELECT COUNT(*) FROM {fixture.QualifiedTimeJobsTable} WHERE \"Status\" IN ('Idle', 'Queued', 'InProgress')"
            );
            unfinished = Convert.ToInt32(await command.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);

            if (unfinished == 0)
            {
                return 0;
            }

            // real-wait: the nodes run the rows on the database clock; the store is the only thing that shows they finished
            await Task.Delay(_Poll, ct);
        }

        return unfinished;
    }

    private static async Task<IReadOnlyList<ClaimFinalRow>> _ReadFinalsAsync(
        IHost host,
        Guid[] ids,
        CancellationToken ct
    )
    {
        var store = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
        List<ClaimFinalRow> finals = [];

        foreach (var chunk in ids.Chunk(500))
        {
            foreach (var row in await store.GetTimeJobsAsync(x => chunk.Contains(x.Id), ct))
            {
                var attempts = await store.GetTimeJobAttemptsAsync(row.Id, ct);
                finals.Add(
                    new ClaimFinalRow(
                        row.Id,
                        row.Status,
                        row.Lease.State,
                        row.RetryCount,
                        row.ExecutedAt is not null,
                        attempts.Count
                    )
                );
            }
        }

        return finals;
    }

    private async Task<DateTime> _StoreUtcNowAsync(CancellationToken ct)
    {
        await using DbConnection connection = fixture.CreateConnection();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {fixture.UtcNowSqlExpression}";
        var value = await command.ExecuteScalarAsync(ct);

        return value switch
        {
            DateTimeOffset offset => offset.UtcDateTime,
            DateTime dateTime => DateTime.SpecifyKind(dateTime, DateTimeKind.Utc),
            _ => throw new InvalidOperationException($"The store clock read returned {value?.GetType().Name}."),
        };
    }
}

[Collection(StoreChaosPostgreSqlCollection.Name)]
public sealed class PostgreSqlClaimRaceTests(StoreChaosPostgreSqlFixture fixture)
    : ClaimRaceScenarioTests<StoreChaosPostgreSqlFixture>(fixture)
{
    [Fact]
    public override Task should_run_every_row_once_and_keep_its_run_state_when_sixteen_claimers_race_an_editor() =>
        base.should_run_every_row_once_and_keep_its_run_state_when_sixteen_claimers_race_an_editor();
}

[Collection(StoreChaosSqlServerCollection.Name)]
public sealed class SqlServerClaimRaceTests(StoreChaosSqlServerFixture fixture)
    : ClaimRaceScenarioTests<StoreChaosSqlServerFixture>(fixture)
{
    [Fact]
    public override Task should_run_every_row_once_and_keep_its_run_state_when_sixteen_claimers_race_an_editor() =>
        base.should_run_every_row_once_and_keep_its_run_state_when_sixteen_claimers_race_an_editor();
}

/// <summary>Everything the claim race records, from every node and mutator, on one process-wide sequence.</summary>
public sealed class ClaimLedger
{
    private readonly ConcurrentQueue<ClaimStart> _starts = new();
    private readonly ConcurrentQueue<ClaimEdit> _edits = new();
    private readonly ConcurrentQueue<ClaimObservation> _observations = new();
    private long _sequence;

    public IReadOnlyList<ClaimStart> Starts => [.. _starts];

    public int StartCount => _starts.Count;

    public IReadOnlyList<ClaimEdit> Edits => [.. _edits];

    public IReadOnlyList<ClaimObservation> Observations => [.. _observations];

    public long Next() => Interlocked.Increment(ref _sequence);

    public void Start(Guid jobId, string node) => _starts.Enqueue(new ClaimStart(jobId, node, Next()));

    public void Edit(ClaimEdit edit) => _edits.Enqueue(edit);

    public void Observe(ClaimObservation observation) => _observations.Enqueue(observation);
}

/// <summary>The claim race's one time-job function: it records its start and returns.</summary>
public sealed class ClaimChaosJobsModule : IJobsModule
{
    public const string Function = "chaos.claim";

    private ClaimChaosJobsModule() { }

    static void IJobsModule.Register(JobsCatalogBuilder catalog)
    {
        catalog.AddFunctions(
            new Dictionary<string, JobFunctionRegistration>(StringComparer.Ordinal)
            {
                [Function] = new JobFunctionRegistration
                {
                    CronExpression = string.Empty,
                    Priority = JobPriority.Normal,
                    Delegate = static (services, context, _) =>
                    {
                        services
                            .GetRequiredService<ClaimLedger>()
                            .Start(context.Id, services.GetService<ChaosNodeName>()?.Value ?? "local");

                        return Task.CompletedTask;
                    },
                    MaxConcurrency = 0,
                },
            }
        );
        catalog.AddDescriptors(
            new Dictionary<string, JobFunctionDescriptor>(StringComparer.Ordinal)
            {
                [Function] = new(Function, null, string.Empty, JobPriority.Normal, 0),
            }
        );
    }
}
