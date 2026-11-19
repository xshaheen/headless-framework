// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Data.Common;
using Headless.Jobs;
using Headless.Jobs.Infrastructure;
using Headless.Sql;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// The peek and the claims read the not-yet-started rows in (ExecutionTime, Id) order. Once the table holds a long
/// history of finished rows, each database must serve those reads from the filtered claimable index rather than walk
/// past the history. The dashboard's last-run read goes the other way, over the finished history itself, and must take
/// it from the last-run index already in order rather than sort it.
/// </summary>
public abstract class JobsClaimIndexConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : class, IJobsCoordinationFixture
{
    private const int _FinishedRows = 5000;
    private const int _DueRows = 5;

    /// <summary>The dialect the provider's native claims render with.</summary>
    protected abstract ISqlDialect Dialect { get; }

    /// <summary>Refreshes the planner's statistics for <paramref name="qualifiedTable"/>.</summary>
    protected abstract Task AnalyzeAsync(DbConnection connection, string qualifiedTable, CancellationToken ct);

    /// <summary>Returns the estimated plan of <paramref name="command"/> as text, without running it.</summary>
    protected abstract Task<string> ExplainAsync(DbCommand command, CancellationToken ct);

    /// <summary>
    /// Steers <paramref name="connection"/> toward an index when a plan test's table is too small for the planner to
    /// prefer one over a full read it can sort in memory; a database that already picks the index leaves it alone.
    /// </summary>
    protected virtual Task PreferIndexAsync(DbConnection connection, CancellationToken ct) => Task.CompletedTask;

    /// <summary>Whether <paramref name="plan"/>, as <see cref="ExplainAsync" /> returned it, holds a sort step.</summary>
    protected abstract bool Sorts(string plan);

    public virtual async Task time_job_peek_reads_the_claimable_index()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        var capture = new ParameterizedSqlCapture();
        using var host = fixture.BuildHost("claim-index-peek", interceptor: capture);
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var indexName = await _SeedHistoryAsync(host, ct);
            capture.Clear();

            var peek = await host
                .Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>()
                .GetEarliestTimeJobsAsync(ct);
            peek.Jobs.Should().HaveCount(_DueRows);

            // The windowed read is the one that pages the due rows in (ExecutionTime, Id) order.
            var windowed = capture.Statements.Last(x => x.Text.Contains("ORDER BY", StringComparison.Ordinal));

            await using var connection = fixture.CreateConnection();
            await connection.OpenAsync(ct);
            await using var command = windowed.CreateCommand(connection);

            (await ExplainAsync(command, ct)).Should().Contain(indexName);
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    public virtual async Task native_fallback_claim_reads_the_claimable_index()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("claim-index-native");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var indexName = await _SeedHistoryAsync(host, ct);

            await using var db = await host
                .Services.GetRequiredService<IDbContextFactory<JobsDbContext>>()
                .CreateDbContextAsync(ct);
            var mapping = TimeJobRelationalMapping.Create<JobsDbContext, TimeJobEntity>(db);

            // The root selection of the native fallback claim: the same predicate builder, order, and batch bound.
            var sql = Dialect.Render(
                new SqlClaimNext(
                    mapping.Table,
                    [mapping.Id],
                    $"""
                    {mapping.ExecutionTime} IS NOT NULL
                      AND {mapping.ExecutionTime} <= {SqlDialectTokens.Now}
                      AND {AcquirePredicateSql.Fallback(mapping.Status, mapping.Lease, reclaimOwn: false)}
                    """,
                    [mapping.ExecutionTime, mapping.Id],
                    $"{mapping.UpdatedAt} = {SqlDialectTokens.Now}",
                    [mapping.Id],
                    "batchSize"
                )
            );

            await using var connection = fixture.CreateConnection();
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            var batchSize = command.CreateParameter();
            batchSize.ParameterName = "batchSize";
            batchSize.Value = JobsClaimStrategyDefaults.MaxCandidatePageSize;
            command.Parameters.Add(batchSize);

            (await ExplainAsync(command, ct)).Should().Contain(indexName);
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    public virtual async Task cron_job_last_run_read_takes_the_finished_history_in_order_from_the_last_run_index()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        var capture = new ParameterizedSqlCapture();
        using var host = fixture.BuildHost("claim-index-last-run", interceptor: capture);
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);

        // The read runs through the store's services alone, so the host's background loops never start.
        var indexName = await _SeedOccurrenceHistoryAsync(host, ct);
        capture.Clear();

        var lastRuns = await host
            .Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>()
            .GetCronJobLastRunsAsync(ct);
        lastRuns.Should().HaveCount(_CronJobs);

        var occurrences = fixture.JobsTable("CronJobOccurrences");
        var read = capture.Statements.Last(x => x.Text.Contains(occurrences, StringComparison.Ordinal));

        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(ct);
        await PreferIndexAsync(connection, ct);
        await using var command = read.CreateCommand(connection);
        var plan = await ExplainAsync(command, ct);

        plan.Should().Contain(indexName);
        Sorts(plan).Should().BeFalse("the index hands the window each cron job's occurrences newest first");
    }

    private const int _CronJobs = 10;
    private const int _OccurrencesPerCronJob = 500;

    // Seeds a long finished history for several cron jobs, each with one occurrence still due, refreshes statistics, and
    // returns the last-run index's name.
    private async Task<string> _SeedOccurrenceHistoryAsync(
        Microsoft.Extensions.Hosting.IHost host,
        CancellationToken ct
    )
    {
        var past = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        List<Guid> cronJobs = [];
        for (var i = 0; i < _CronJobs; i++)
        {
            var id = Guid.NewGuid();
            await fixture.SeedCronJobAsync(id, "last-run-index", "0 0 * * * *", NodeDeathPolicy.Retry, ct);
            cronJobs.Add(id);
        }

        await using var db = await host
            .Services.GetRequiredService<IDbContextFactory<JobsDbContext>>()
            .CreateDbContextAsync(ct);

        foreach (var cronJobId in cronJobs)
        {
            for (var i = 0; i <= _OccurrencesPerCronJob; i++)
            {
                var finished = i < _OccurrencesPerCronJob;
                db.Set<CronJobOccurrenceEntity<CronJobEntity>>()
                    .Add(
                        new CronJobOccurrenceEntity<CronJobEntity>
                        {
                            Id = Guid.NewGuid(),
                            CronJobId = cronJobId,
                            Function = "last-run-index",
                            Status = finished ? JobStatus.Succeeded : JobStatus.Idle,
                            ExecutionTime = past.AddMinutes(i),
                            ExecutedAt = finished ? new DateTimeOffset(past.AddMinutes(i)) : null,
                            CreatedAt = new DateTimeOffset(past),
                            UpdatedAt = new DateTimeOffset(past),
                        }
                    );
            }
        }

        await db.SaveChangesAsync(ct);

        await using (var connection = fixture.CreateConnection())
        {
            await connection.OpenAsync(ct);
            await AnalyzeAsync(connection, fixture.QualifiedCronJobOccurrencesTable, ct);
        }

        return db
            .Model.FindEntityType(typeof(CronJobOccurrenceEntity<CronJobEntity>))!
            .GetIndexes()
            .Single(index =>
                index
                    .Properties.Select(p => p.Name)
                    .Take(3)
                    .SequenceEqual([
                        nameof(CronJobOccurrenceEntity<>.CronJobId),
                        nameof(CronJobOccurrenceEntity<>.ExecutionTime),
                        nameof(CronJobOccurrenceEntity<>.Id),
                    ])
            )
            .GetDatabaseName()!;
    }

    // Seeds a long finished history and a few due rows, refreshes statistics, and returns the claimable index's name.
    private async Task<string> _SeedHistoryAsync(Microsoft.Extensions.Hosting.IHost host, CancellationToken ct)
    {
        await using var db = await host
            .Services.GetRequiredService<IDbContextFactory<JobsDbContext>>()
            .CreateDbContextAsync(ct);

        // Finished rows sit in the past; the due rows share one whole second a minute ahead, because the peek reaches back
        // only one second and pages a single second of rows.
        var now = DateTime.UtcNow;
        var past = now.AddHours(-1);
        var dueSecond = new DateTime(
            now.Year,
            now.Month,
            now.Day,
            now.Hour,
            now.Minute,
            now.Second,
            DateTimeKind.Utc
        ).AddMinutes(1);
        for (var i = 0; i < _FinishedRows + _DueRows; i++)
        {
            db.Set<TimeJobEntity>()
                .Add(
                    new TimeJobEntity
                    {
                        Id = Guid.NewGuid(),
                        Function = "claim-index",
                        Status = i < _FinishedRows ? JobStatus.Succeeded : JobStatus.Idle,
                        ExecutionTime =
                            i < _FinishedRows ? past.AddMilliseconds(i) : dueSecond.AddMilliseconds(i - _FinishedRows),
                        CreatedAt = new DateTimeOffset(past),
                        UpdatedAt = new DateTimeOffset(past),
                    }
                );
        }

        await db.SaveChangesAsync(ct);

        await using (var connection = fixture.CreateConnection())
        {
            await connection.OpenAsync(ct);
            await AnalyzeAsync(connection, fixture.QualifiedTimeJobsTable, ct);
        }

        return db
            .Model.FindEntityType(typeof(TimeJobEntity))!
            .GetIndexes()
            .Single(index =>
                index.GetFilter() is not null
                && index.Properties.Select(p => p.Name).SequenceEqual([nameof(TimeJobEntity.ExecutionTime), "Id"])
            )
            .GetDatabaseName()!;
    }

    /// <summary>Records each reader command EF runs with its parameters, so a test can replay it under EXPLAIN.</summary>
    private sealed class ParameterizedSqlCapture : DbCommandInterceptor
    {
        private readonly ConcurrentQueue<CapturedCommand> _statements = new();

        public CapturedCommand[] Statements => [.. _statements];

        public void Clear()
        {
            while (_statements.TryDequeue(out _)) { }
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            _statements.Enqueue(CapturedCommand.From(command));
            return ValueTask.FromResult(result);
        }
    }

    private sealed record CapturedCommand(
        string Text,
        IReadOnlyList<(string Name, object? Value, System.Data.DbType Type)> Parameters
    )
    {
        public static CapturedCommand From(DbCommand command) =>
            new(
                command.CommandText,
                [.. command.Parameters.Cast<DbParameter>().Select(p => (p.ParameterName, p.Value, p.DbType))]
            );

        public DbCommand CreateCommand(DbConnection connection)
        {
            var command = connection.CreateCommand();
            command.CommandText = Text;
            foreach (var (name, value, type) in Parameters)
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = name;
                parameter.Value = value ?? DBNull.Value;
                parameter.DbType = type;
                command.Parameters.Add(parameter);
            }

            return command;
        }
    }
}
