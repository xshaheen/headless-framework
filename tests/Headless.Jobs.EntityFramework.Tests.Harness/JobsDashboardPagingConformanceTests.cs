// Copyright (c) Mahmoud Shaheen. All rights reserved.

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

    private static IJobPersistenceProvider<TimeJobEntity, CronJobEntity> _Store(IHost host) =>
        host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
}
