// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using System.Globalization;
using Headless.Jobs;
using Headless.Testing.Tests;
using Microsoft.Extensions.Hosting;
using Tests.Scheduling;

namespace Tests.Scale;

/// <summary>
/// S8, the fallback sweep at scale: a store holding a long finished history (smoke 100k, full 1M rows in each of the
/// time-job and cron-occurrence tables) runs one real Jobs host, background services on, while the scenario captures the
/// plan of every statement the store executes. No statement may read a jobs table end to end.
/// </summary>
/// <remarks>
/// <para>
/// The host exercises the paths the issue names on its own: the scheduler's peek and claim of a few due rows, and the
/// fallback service's passes, each second here, which include the orphan-owner scan
/// (<c>GetActiveOwnerIdsAsync</c>) and, because a few rows are seeded held by a node no membership lists, the dead-node
/// release (<c>ReleaseDeadNodeResources</c>). The scenario waits until the due rows ran and the orphaned rows were
/// released, so each of those paths provably ran inside the capture.
/// </para>
/// <para>
/// The capture is on the server, not in the client, because the claims, lease transitions, and fenced writes run as raw
/// SQL on the context's connection and bypass EF Core interceptors. PostgreSQL loads <c>auto_explain</c> into every
/// session of the scenario's database and logs each statement's executed plan; SQL Server keeps each statement's plan
/// in its plan cache, read back from <c>sys.dm_exec_query_stats</c>. Either way the plans judged are the ones the store
/// actually ran, with the parameters it was given, not plans re-derived from source text.
/// </para>
/// </remarks>
public abstract class FallbackSweepScaleTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : class, IStoreChaosFixture
{
    private const string _Scenario = "S8 fallback sweep at scale";
    private const string _Ghost = JobsCoordinationFixtureExtensions.ClusterName + "/ghost-node@1";
    private const int _DueRows = 5;
    private const int _OrphanRows = 3;
    private static readonly TimeSpan _Poll = TimeSpan.FromMilliseconds(250);
    private const int _Polls = 240;

    /// <summary>The store the scenario runs on.</summary>
    protected TFixture Fixture => fixture;

    /// <summary>The table names the plans must never scan, as the plans spell them.</summary>
    protected abstract IReadOnlyCollection<string> JobsTables { get; }

    /// <summary>Inserts <c>@rows</c> finished time jobs in one statement.</summary>
    protected abstract string SeedTimeJobHistorySql { get; }

    /// <summary>Inserts <c>@rows</c> finished occurrences of the definition <c>@cronJobId</c> in one statement.</summary>
    protected abstract string SeedOccurrenceHistorySql { get; }

    /// <summary>Refreshes the planner's statistics for the jobs tables.</summary>
    protected abstract Task AnalyzeAsync(DbConnection connection, CancellationToken ct);

    /// <summary>Starts recording the plan of every statement new sessions run.</summary>
    protected abstract Task BeginCaptureAsync(CancellationToken ct);

    /// <summary>Stops recording, so the next scenario on this server runs unobserved.</summary>
    protected abstract Task EndCaptureAsync(CancellationToken ct);

    /// <summary>Every plan recorded since <see cref="BeginCaptureAsync"/>.</summary>
    protected abstract Task<IReadOnlyList<CapturedPlan>> ReadPlansAsync(CancellationToken ct);

    /// <summary>The full scans of <see cref="JobsTables"/> in one plan.</summary>
    protected abstract IReadOnlyList<string> FindScans(CapturedPlan plan);

    public virtual async Task should_read_no_jobs_table_end_to_end_when_the_store_holds_a_long_finished_history()
    {
        var ct = AbortToken;
        var settings = ChaosRunSettings.Current;
        var rows = settings.Pick(100_000, 1_000_000);
        var output = TestContext.Current.TestOutputHelper;
        output?.WriteLine(
            string.Create(CultureInfo.InvariantCulture, $"{_Scenario}: {settings}, {rows} finished rows per table")
        );

        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost(
            "fallback-scale",
            configureJobs: jobs =>
                jobs.ConfigureScheduler(scheduler =>
                {
                    scheduler.FallbackIntervalChecker = TimeSpan.FromSeconds(1);
                    scheduler.DeadNodeReconcileInterval = TimeSpan.FromSeconds(1);
                }),
            runBackgroundServices: true
        );
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);

        var cronJobId = Guid.NewGuid();
        await fixture.SeedCronJobAsync(
            cronJobId,
            JobsCoordinationFixtureExtensions.CoordinatedFunctionName,
            "0 0 0 1 1 *",
            NodeDeathPolicy.Retry,
            ct,
            nextDueOffsetSeconds: 86_400 * 30
        );
        await _SeedHistoryAsync(rows, cronJobId, ct);

        var storeNow = await _StoreUtcNowAsync(ct);
        var due = Enumerable.Range(0, _DueRows).Select(_ => Guid.NewGuid()).ToList();
        var orphaned = Enumerable.Range(0, _OrphanRows).Select(_ => Guid.NewGuid()).ToList();

        foreach (var id in due)
        {
            await fixture.SeedTimeJobAsync(
                id,
                JobsCoordinationFixtureExtensions.CoordinatedFunctionName,
                (int)JobStatus.Idle,
                ownerId: null,
                ct,
                executionTime: storeNow.AddSeconds(-1)
            );
        }

        // A chain child gated on its parent's success, both rows finished and due: its claim must evaluate the
        // run-condition gate, so the plan of that branch is not only planned but executed.
        var parent = Guid.NewGuid();
        var gated = Guid.NewGuid();
        await fixture.SeedTimeJobAsync(
            parent,
            JobsCoordinationFixtureExtensions.CoordinatedFunctionName,
            (int)JobStatus.Succeeded,
            ownerId: null,
            ct,
            executionTime: storeNow.AddMinutes(-5)
        );
        await fixture.SeedTimeJobAsync(
            gated,
            JobsCoordinationFixtureExtensions.CoordinatedFunctionName,
            (int)JobStatus.Idle,
            ownerId: null,
            ct,
            executionTime: storeNow.AddSeconds(-1)
        );
        await _ExecuteAsync(
            fixture.Sql(
                $"UPDATE {fixture.QualifiedTimeJobsTable} SET \"ParentId\" = '{parent}', \"RunCondition\" = 'OnSuccess' WHERE \"Id\" = '{gated}'"
            ),
            ct
        );
        due.Add(gated);

        foreach (var id in orphaned)
        {
            // Queued under a node no membership lists, with a lease far from lapsing: only the orphan-owner sweep
            // frees it, not the lapsed-lease reclaim.
            await fixture.SeedTimeJobAsync(
                id,
                JobsCoordinationFixtureExtensions.CoordinatedFunctionName,
                (int)JobStatus.Queued,
                _Ghost,
                ct,
                lockedUntil: storeNow.AddDays(1),
                executionTime: storeNow.AddHours(1)
            );
        }

        await using (var connection = fixture.CreateConnection())
        {
            await connection.OpenAsync(ct);
            await AnalyzeAsync(connection, ct);
        }

        List<string> violations = [];
        IReadOnlyList<CapturedPlan> plans;
        await BeginCaptureAsync(ct);

        try
        {
            await host.StartAsync(ct);

            try
            {
                var pending = await _WaitAsync(due, orphaned, ct);

                if (pending is not null)
                {
                    violations.Add($"the host did not run the paths under test: {pending}");
                }
            }
            finally
            {
                await host.StopAsync(ct);
            }

            plans = await ReadPlansAsync(ct);
        }
        finally
        {
            await EndCaptureAsync(ct);
        }

        var judged = plans
            .Where(plan =>
                !plan.QueryText.Contains(PlanScanOracle.ProbeMarker, StringComparison.Ordinal)
                && JobsTables.Any(table => plan.Plan.Contains(table, StringComparison.Ordinal))
            )
            .ToList();
        violations.AddRange(PlanScanOracle.Violations(judged, FindScans));

        if (judged.Count == 0)
        {
            violations.Add("the capture recorded no plan that reads a jobs table, so the scenario judged nothing");
        }

        var knownScans = violations.Where(v => KnownDefects.Any(d => v.Contains(d, StringComparison.Ordinal))).ToList();
        var onlyKnown = violations.Count > 0 && knownScans.Count == violations.Count;

        ChaosReport.Record(
            fixture.Store,
            _Scenario,
            violations,
            string.Create(
                CultureInfo.InvariantCulture,
                $"rows {rows} per table, plans captured {plans.Count}, on jobs tables {judged.Count}, full scans {violations.Count}"
            ),
            string.Join(
                "\n\n",
                judged.Where(plan => FindScans(plan).Count > 0).Select(plan => $"{plan.QueryText}\n{plan.Plan}")
            ),
            line => output?.WriteLine(line),
            quarantinedBy: onlyKnown ? QuarantineIssue : null
        );

        violations.Except(knownScans, StringComparer.Ordinal).Should().BeEmpty();

        if (knownScans.Count > 0)
        {
            // quarantine: #1750 only the scans KnownDefects names remain; any other violation failed above
            Assert.Skip($"Quarantined known defect: {string.Join("; ", knownScans)}");
        }
    }

    /// <summary>
    /// Fragments of violations a known, reported framework defect produces on this store. The scenario still fails on
    /// any other violation and skips, visibly, while only these remain.
    /// </summary>
    protected virtual IReadOnlyList<string> KnownDefects => [];

    /// <summary>The issue that tracks <see cref="KnownDefects"/>, which the run summary names on a quarantined row.</summary>
    protected virtual string? QuarantineIssue => null;

    // Null once every due row ran and no row is still held by the ghost owner; otherwise what is still pending.
    private async Task<string?> _WaitAsync(List<Guid> due, List<Guid> orphaned, CancellationToken ct)
    {
        var pending = string.Empty;

        for (var poll = 0; poll < _Polls; poll++)
        {
            await using var connection = fixture.CreateConnection();
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = fixture.Sql(
                $"/* {PlanScanOracle.ProbeMarker} */ SELECT "
                    + $"(SELECT COUNT(*) FROM {fixture.QualifiedTimeJobsTable} WHERE \"Id\" IN ({_List(due)}) AND \"Status\" NOT IN ('Succeeded', 'DueDone')), "
                    + $"(SELECT COUNT(*) FROM {fixture.QualifiedTimeJobsTable} WHERE \"Id\" IN ({_List(orphaned)}) AND \"LeaseHolder\" = '{_Ghost}' AND \"LeaseState\" = 1)"
            );
            await using var reader = await command.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            var notRun = Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture);
            var stillHeld = Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture);

            if (notRun == 0 && stillHeld == 0)
            {
                return null;
            }

            pending = string.Create(
                CultureInfo.InvariantCulture,
                $"{notRun} due row(s) not run, {stillHeld} row(s) still held by {_Ghost}"
            );
            // real-wait: the host runs its passes on the database clock; the store is the only thing that shows they ran
            await Task.Delay(_Poll, ct);
        }

        return pending;
    }

    private async Task _SeedHistoryAsync(int rows, Guid cronJobId, CancellationToken ct)
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(ct);

        foreach (var sql in new[] { SeedTimeJobHistorySql, SeedOccurrenceHistorySql })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = 600;
            _AddParameter(command, "@rows", rows);
            _AddParameter(command, "@cronJobId", cronJobId);
            await command.ExecuteNonQueryAsync(ct);
        }
    }

    private async Task _ExecuteAsync(string sql, CancellationToken ct)
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(ct);
    }

    private static void _AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static string _List(List<Guid> ids) => string.Join(", ", ids.Select(id => $"'{id}'"));

    private async Task<DateTime> _StoreUtcNowAsync(CancellationToken ct)
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"/* {PlanScanOracle.ProbeMarker} */ SELECT {fixture.UtcNowSqlExpression}";
        var value = await command.ExecuteScalarAsync(ct);

        return value switch
        {
            DateTimeOffset offset => offset.UtcDateTime,
            DateTime dateTime => DateTime.SpecifyKind(dateTime, DateTimeKind.Utc),
            _ => throw new InvalidOperationException($"The store clock read returned {value?.GetType().Name}."),
        };
    }
}
