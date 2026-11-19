// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using System.Globalization;
using Microsoft.Data.SqlClient;
using Npgsql;
using Tests.Scheduling;

namespace Tests.Scale;

/// <summary>
/// S8 on PostgreSQL. <c>auto_explain</c> is loaded into every new session of the scenario's database for the capture
/// window and logs each statement's executed plan, with actual row counts (<c>log_analyze</c>), to the server log, which
/// the scenario reads back from the container.
/// </summary>
[Collection(StoreChaosPostgreSqlCollection.Name)]
public sealed class PostgreSqlFallbackSweepScaleTests(StoreChaosPostgreSqlFixture fixture)
    : FallbackSweepScaleTests<StoreChaosPostgreSqlFixture>(fixture)
{
    private string? _marker;

    protected override IReadOnlyCollection<string> JobsTables { get; } = ["time_jobs", "cron_job_occurrences"];

    // One row in ten failed, the rest succeeded: a finished history the claimable and owner indexes must step past.
    protected override string SeedTimeJobHistorySql =>
        """
            INSERT INTO headless.time_jobs ("id", "function", "status", "created_at", "updated_at", "elapsed_time",
                "retries", "retry_count", "on_node_death", "lease_generation", "lease_state", "lease_expires_at",
                "lease_holder", "lease_takeover_count", "contract_version", "execution_time", "executed_at")
            SELECT gen_random_uuid(), 'chaos.history', CASE WHEN g % 10 = 0 THEN 'Failed' ELSE 'Succeeded' END,
                now() - interval '30 days', now() - interval '30 days', 0, 0, 0, 'Retry', 1, 2, NULL,
                'jobs-it/history-node@1', 0, '1', now() - interval '30 days' + g * interval '1 millisecond',
                now() - interval '30 days'
            FROM generate_series(1, @rows) AS g;
            """;

    protected override string SeedOccurrenceHistorySql =>
        """
            INSERT INTO headless.cron_job_occurrences ("id", "cron_job_id", "status", "execution_time", "created_at",
                "updated_at", "elapsed_time", "retry_count", "on_node_death", "lease_generation", "lease_state",
                "lease_expires_at", "lease_holder", "lease_takeover_count", "disposition", "function",
                "contract_version", "request")
            SELECT gen_random_uuid(), c."id", CASE WHEN g % 10 = 0 THEN 'Skipped' ELSE 'Succeeded' END,
                now() - interval '60 days' + g * interval '1 second', now() - interval '60 days',
                now() - interval '60 days', 0, 0, 'Retry', 1, 2, NULL, 'jobs-it/history-node@1', 0, 'Accounted',
                c."function", c."contract_version", c."request"
            FROM generate_series(1, @rows) AS g
            CROSS JOIN headless.cron_jobs AS c
            WHERE c."id" = @cronJobId;
            """;

    [Fact]
    public override Task should_read_no_jobs_table_end_to_end_when_the_store_holds_a_long_finished_history() =>
        base.should_read_no_jobs_table_end_to_end_when_the_store_holds_a_long_finished_history();

    // quarantine: #1750 the native claim's timed-child gate (TimedChildGateSql) reads the parent through an uncorrelated
    // IN subquery, which PostgreSQL plans as a hashed SubPlan over a Seq Scan of the whole time-job table; whenever a
    // gated child is a candidate, each claim reads every finished row (90,001 at the smoke size) to admit it
    protected override IReadOnlyList<string> KnownDefects { get; } = ["Seq Scan on time_jobs gate_parent"];

    protected override string QuarantineIssue => "#1750";

    protected override async Task AnalyzeAsync(DbConnection connection, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "ANALYZE headless.time_jobs; ANALYZE headless.cron_job_occurrences;";
        command.CommandTimeout = 600;
        await command.ExecuteNonQueryAsync(ct);
    }

    protected override async Task BeginCaptureAsync(CancellationToken ct)
    {
        await _ExecuteAsync(
            $"""
            ALTER DATABASE "{Fixture.DatabaseName}" SET session_preload_libraries = 'auto_explain';
            ALTER DATABASE "{Fixture.DatabaseName}" SET auto_explain.log_min_duration = 0;
            ALTER DATABASE "{Fixture.DatabaseName}" SET auto_explain.log_format = 'text';
            ALTER DATABASE "{Fixture.DatabaseName}" SET auto_explain.log_analyze = on;
            ALTER DATABASE "{Fixture.DatabaseName}" SET auto_explain.log_timing = off;
            """,
            ct
        );

        // Pooled sessions opened before the change never load the library, so every later statement must run on a
        // session opened after it.
        NpgsqlConnection.ClearAllPools();

        // The log holds every earlier scenario's lines too; the capture starts at this marker's plan.
        _marker = $"chaos-capture-{Guid.NewGuid():N}";
        await _ExecuteAsync($"SELECT '{_marker}'", ct);
    }

    protected override async Task EndCaptureAsync(CancellationToken ct)
    {
        await _ExecuteAsync(
            $"""
            ALTER DATABASE "{Fixture.DatabaseName}" RESET session_preload_libraries;
            ALTER DATABASE "{Fixture.DatabaseName}" RESET auto_explain.log_min_duration;
            ALTER DATABASE "{Fixture.DatabaseName}" RESET auto_explain.log_format;
            ALTER DATABASE "{Fixture.DatabaseName}" RESET auto_explain.log_analyze;
            ALTER DATABASE "{Fixture.DatabaseName}" RESET auto_explain.log_timing;
            """,
            ct
        );
        NpgsqlConnection.ClearAllPools();
    }

    protected override async Task<IReadOnlyList<CapturedPlan>> ReadPlansAsync(CancellationToken ct)
    {
        var (stdout, stderr) = await Fixture.Container.GetLogsAsync(timestampsEnabled: false, ct: ct);
        var log = stdout + "\n" + stderr;
        var start = _marker is null ? -1 : log.LastIndexOf(_marker, StringComparison.Ordinal);

        if (start < 0)
        {
            throw new InvalidOperationException(
                "The server log holds no capture marker: auto_explain did not load into the scenario's sessions."
            );
        }

        return PlanScanOracle.ParseAutoExplainLog(log[start..]);
    }

    protected override IReadOnlyList<string> FindScans(CapturedPlan plan) =>
        PlanScanOracle.PostgreSqlScans(plan, JobsTables);

    private async Task _ExecuteAsync(string sql, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(Fixture.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(ct);
    }
}

/// <summary>
/// S8 on SQL Server. The plan cache is cleared when the capture starts, and every statement compiled after it is read
/// back from <c>sys.dm_exec_query_stats</c> with its cached showplan.
/// </summary>
[Collection(StoreChaosSqlServerCollection.Name)]
public sealed class SqlServerFallbackSweepScaleTests(StoreChaosSqlServerFixture fixture)
    : FallbackSweepScaleTests<StoreChaosSqlServerFixture>(fixture)
{
    protected override IReadOnlyCollection<string> JobsTables { get; } = ["TimeJobs", "CronJobOccurrences"];

    private const string _Numbers =
        "WITH n AS (SELECT TOP (@rows) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS g "
        + "FROM sys.all_objects AS a CROSS JOIN sys.all_objects AS b)";

    protected override string SeedTimeJobHistorySql =>
        _Numbers
        + """
            INSERT INTO [headless].[TimeJobs] ([Id], [Function], [Status], [CreatedAt], [UpdatedAt], [ElapsedTime],
                [Retries], [RetryCount], [OnNodeDeath], [LeaseGeneration], [LeaseState], [LeaseExpiresAt],
                [LeaseHolder], [LeaseTakeoverCount], [ContractVersion], [ExecutionTime], [ExecutedAt])
            SELECT NEWID(), 'chaos.history', CASE WHEN g % 10 = 0 THEN 'Failed' ELSE 'Succeeded' END,
                DATEADD(day, -30, SYSUTCDATETIME()), DATEADD(day, -30, SYSUTCDATETIME()), 0, 0, 0, 'Retry', 1, 2,
                NULL, 'jobs-it/history-node@1', 0, '1', DATEADD(millisecond, g, DATEADD(day, -30, SYSUTCDATETIME())),
                DATEADD(day, -30, SYSUTCDATETIME())
            FROM n;
            """;

    protected override string SeedOccurrenceHistorySql =>
        _Numbers
        + """
            INSERT INTO [headless].[CronJobOccurrences] ([Id], [CronJobId], [Status], [ExecutionTime], [CreatedAt],
                [UpdatedAt], [ElapsedTime], [RetryCount], [OnNodeDeath], [LeaseGeneration], [LeaseState],
                [LeaseExpiresAt], [LeaseHolder], [LeaseTakeoverCount], [Disposition], [Function],
                [ContractVersion], [Request])
            SELECT NEWID(), c.[Id], CASE WHEN g % 10 = 0 THEN 'Skipped' ELSE 'Succeeded' END,
                DATEADD(second, g, DATEADD(day, -60, SYSUTCDATETIME())), DATEADD(day, -60, SYSUTCDATETIME()),
                DATEADD(day, -60, SYSUTCDATETIME()), 0, 0, 'Retry', 1, 2, NULL, 'jobs-it/history-node@1', 0,
                'Accounted', c.[Function], c.[ContractVersion], c.[Request]
            FROM n CROSS JOIN [headless].[CronJobs] AS c
            WHERE c.[Id] = @cronJobId;
            """;

    [Fact]
    public override Task should_read_no_jobs_table_end_to_end_when_the_store_holds_a_long_finished_history() =>
        base.should_read_no_jobs_table_end_to_end_when_the_store_holds_a_long_finished_history();

    protected override async Task AnalyzeAsync(DbConnection connection, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            "UPDATE STATISTICS [headless].[TimeJobs] WITH FULLSCAN; UPDATE STATISTICS [headless].[CronJobOccurrences] WITH FULLSCAN;";
        command.CommandTimeout = 600;
        await command.ExecuteNonQueryAsync(ct);
    }

    protected override Task BeginCaptureAsync(CancellationToken ct) => _ExecuteAsync("DBCC FREEPROCCACHE;", ct);

    // The plan cache needs no switch to turn off.
    protected override Task EndCaptureAsync(CancellationToken ct) => Task.CompletedTask;

    protected override async Task<IReadOnlyList<CapturedPlan>> ReadPlansAsync(CancellationToken ct)
    {
        await using var connection = new SqlConnection(Fixture.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            /* {PlanScanOracle.ProbeMarker} */
            SELECT st.text, CAST(qp.query_plan AS nvarchar(max))
            FROM (SELECT DISTINCT plan_handle, sql_handle FROM sys.dm_exec_query_stats) AS qs
            CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) AS st
            CROSS APPLY sys.dm_exec_query_plan(qs.plan_handle) AS qp
            WHERE qp.query_plan IS NOT NULL
              AND (st.text LIKE '%TimeJobs%' OR st.text LIKE '%CronJobOccurrences%');
            """;
        List<CapturedPlan> plans = [];
        await using var reader = await command.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            plans.Add(new CapturedPlan(reader.GetString(0), reader.GetString(1)));
        }

        return plans;
    }

    protected override IReadOnlyList<string> FindScans(CapturedPlan plan) =>
        PlanScanOracle.SqlServerScans(plan, JobsTables);

    private async Task _ExecuteAsync(string sql, CancellationToken ct)
    {
        await using var connection = new SqlConnection(Fixture.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(ct);
    }
}
