// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using Headless.Jobs;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

/// <summary>
/// Dashboard pages over rows that share their sort key: a batch enqueue stamps one execution time on every root and a
/// seed stamps one creation time on every definition. Paging through them must visit each row exactly once, in the
/// order a single page holding them all reports, so the operator never sees a job twice or misses one.
/// </summary>
public abstract class JobsDashboardPagingConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : class, IJobsCoordinationFixture
{
    private const string _Function = "paging-job";
    private const int _RowCount = 40;
    private const int _PageSize = 10;

    public virtual async Task time_job_pages_over_one_execution_time_visit_every_root_once()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("paging-reader");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var executionTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < _RowCount; i++)
        {
            await fixture.SeedTimeJobAsync(
                Guid.NewGuid(),
                _Function,
                (int)JobStatus.Succeeded,
                ownerId: null,
                ct,
                executionTime: executionTime
            );
        }

        var store = _Store(host);
        var single = await store.GetTimeJobsPaginatedAsync(null, 1, _RowCount, ct);
        var paged = new List<Guid>();
        for (var page = 1; page <= _RowCount / _PageSize; page++)
        {
            var result = await store.GetTimeJobsPaginatedAsync(null, page, _PageSize, ct);
            paged.AddRange(result.Items.Select(x => x.Id));
        }

        single.Items.Should().HaveCount(_RowCount);
        paged.Should().Equal(single.Items.Select(x => x.Id));
    }

    public virtual async Task cron_job_pages_over_one_creation_time_visit_every_definition_once()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("paging-reader");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        for (var i = 0; i < _RowCount; i++)
        {
            await fixture.SeedCronJobAsync(Guid.NewGuid(), $"{_Function}-{i}", "0 0 * * *", NodeDeathPolicy.Retry, ct);
        }

        // One statement reads the store clock once, so every definition shares the creation time a seed stamps.
        await using (var connection = fixture.CreateConnection())
        {
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = fixture.Sql(
                $"UPDATE {fixture.QualifiedCronJobsTable} SET \"CreatedAt\" = {fixture.UtcNowSqlExpression};"
            );
            await command.ExecuteNonQueryAsync(ct);
        }

        var store = _Store(host);
        var single = await store.GetCronJobsPaginatedAsync(null, 1, _RowCount, ct);
        var paged = new List<Guid>();
        for (var page = 1; page <= _RowCount / _PageSize; page++)
        {
            var result = await store.GetCronJobsPaginatedAsync(null, page, _PageSize, ct);
            paged.AddRange(result.Items.Select(x => x.Id));
        }

        single.Items.Should().HaveCount(_RowCount);
        paged.Should().Equal(single.Items.Select(x => x.Id));
    }

    public virtual async Task run_list_pages_sorted_by_function_or_status_visit_every_tied_run_once_in_one_order()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("paging-reader");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var store = _Store(host);
        var cronJobId = Guid.NewGuid();
        await fixture.SeedCronJobAsync(cronJobId, "beta.job", "0 0 * * * *", NodeDeathPolicy.Retry, ct);
        var at = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        // Lower-case ASCII names sort alike in every collation, so the order can be checked ordinally. Statuses are
        // terminal so no scheduler claims a row while the pages are read.
        string[] functions = ["alpha.job", "beta.job", "gamma.job"];
        JobStatus[] statuses = [JobStatus.Succeeded, JobStatus.Failed, JobStatus.Cancelled, JobStatus.Skipped];
        var jobs = Enumerable
            .Range(0, 12)
            .Select(index => new TimeJobEntity
            {
                Id = Guid.NewGuid(),
                Function = functions[index % functions.Length],
                Status = statuses[index % statuses.Length],
                ExecutionTime = at.AddMinutes(index),
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            })
            .ToArray();
        await store.AddTimeJobsAsync(jobs, ct);
        var occurrences = Enumerable
            .Range(0, 6)
            .Select(index => new CronJobOccurrenceEntity<CronJobEntity>
            {
                Id = Guid.NewGuid(),
                CronJobId = cronJobId,
                Status = statuses[index % 2],
                ExecutionTime = at.AddHours(index + 1),
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            })
            .ToArray();
        await store.InsertCronJobOccurrencesAsync(occurrences, ct);

        foreach (var sortBy in new[] { JobRunSortKey.Function, JobRunSortKey.Status })
        {
            foreach (var direction in new[] { ListSortDirection.Ascending, ListSortDirection.Descending })
            {
                var query = new JobRunQuery { SortBy = sortBy, SortDirection = direction };
                var single = await store.ListJobRunsAsync(query, after: null, pageSize: 100, ct);
                // A page of 4 splits every group of tied runs, in both tables.
                var paged = await _ReadAllPagesAsync(store, query, pageSize: 4, ct);

                single.Runs.Should().HaveCount(18);
                paged
                    .Should()
                    .Equal(single.Runs, static (actual, expected) => actual.Id == expected.Id, $"{sortBy} {direction}");
                string[] keys =
                    sortBy == JobRunSortKey.Function
                        ? [.. paged.Select(x => x.Function)]
                        : [.. paged.Select(x => ((int)x.Status).ToString("D2", CultureInfo.InvariantCulture))];

                if (direction == ListSortDirection.Ascending)
                {
                    keys.Should().BeInAscendingOrder(StringComparer.Ordinal);
                }
                else
                {
                    keys.Should().BeInDescendingOrder(StringComparer.Ordinal);
                }
            }
        }
    }

    private static async Task<List<JobRunSummary>> _ReadAllPagesAsync(
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> store,
        JobRunQuery query,
        int pageSize,
        CancellationToken cancellationToken
    )
    {
        var runs = new List<JobRunSummary>();
        JobRunCursor? cursor = null;

        do
        {
            var page = await store.ListJobRunsAsync(query, cursor, pageSize, cancellationToken);
            runs.AddRange(page.Runs);
            // The cursor crosses HTTP as text between pages.
            cursor = page.Next is { } next && JobRunCursor.TryParse(next.ToString(), out var parsed) ? parsed : null;
        } while (cursor is not null);

        return runs;
    }

    private static IJobPersistenceProvider<TimeJobEntity, CronJobEntity> _Store(IHost host) =>
        host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
}
