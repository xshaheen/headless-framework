// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Dapper;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Persistence;
using Headless.Messaging.PostgreSql;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Tests.Scheduling;

[Collection<PostgreSqlTestFixture>]
public sealed class PostgreSqlScheduledJobStorageTest(PostgreSqlTestFixture fixture) : TestBase
{
    private PostgreSqlScheduledJobStorage _storage = null!;
    private IStorageInitializer _initializer = null!;

    public override async ValueTask InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.AddLogging();
        services.Configure<PostgreSqlOptions>(x => x.ConnectionString = fixture.ConnectionString);
        services.Configure<MessagingOptions>(x => x.Version = "v1");
        services.AddSingleton<IStorageInitializer, PostgreSqlStorageInitializer>();
        services.AddSingleton(TimeProvider.System);

        var provider = services.BuildServiceProvider();
        _initializer = provider.GetRequiredService<IStorageInitializer>();
        await _initializer.InitializeAsync(AbortToken);

        _storage = new PostgreSqlScheduledJobStorage(
            provider.GetRequiredService<IOptions<PostgreSqlOptions>>(),
            _initializer,
            TimeProvider.System
        );

        await base.InitializeAsync();
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        try
        {
            await using var connection = new NpgsqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            await connection.ExecuteAsync(
                """
                TRUNCATE TABLE messaging.job_executions;
                TRUNCATE TABLE messaging.scheduled_jobs CASCADE;
                """
            );
        }
        catch (PostgresException)
        {
            // Schema may not exist if test failed before initialization
        }

        await base.DisposeAsyncCore();
    }

    [Fact]
    public async Task should_upsert_new_job()
    {
        // given
        var job = _CreateJob("upsert-new");

        // when
        await _storage.UpsertJobAsync(job, AbortToken);

        // then
        var result = await _storage.GetJobByNameAsync("upsert-new", AbortToken);
        result.Should().NotBeNull();
        result!.Name.Should().Be("upsert-new");
        result.Type.Should().Be(ScheduledJobType.Recurring);
        result.CronExpression.Should().Be("*/5 * * * *");
        result.Status.Should().Be(ScheduledJobStatus.Pending);
        result.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task should_upsert_update_existing_job_by_name()
    {
        // given
        var job = _CreateJob("upsert-update");
        await _storage.UpsertJobAsync(job, AbortToken);

        // when - upsert with same name but different cron and new id
        var updated = _CreateJob("upsert-update");
        updated.Id = Guid.NewGuid();
        updated.CronExpression = "0 * * * *";
        updated.SkipIfRunning = false;
        await _storage.UpsertJobAsync(updated, AbortToken);

        // then - should have updated the existing row, not inserted a new one
        var all = await _storage.GetAllJobsAsync(AbortToken);
        var matches = all.Where(j => j.Name == "upsert-update").ToList();
        matches.Should().HaveCount(1);
        matches[0].CronExpression.Should().Be("0 * * * *");
        matches[0].SkipIfRunning.Should().BeFalse();
    }

    [Fact]
    public async Task should_get_job_by_name()
    {
        // given
        var job = _CreateJob("get-by-name");
        await _storage.UpsertJobAsync(job, AbortToken);

        // when
        var result = await _storage.GetJobByNameAsync("get-by-name", AbortToken);

        // then
        result.Should().NotBeNull();
        result!.Id.Should().Be(job.Id);
        result.Name.Should().Be("get-by-name");
    }

    [Fact]
    public async Task should_return_null_when_job_not_found()
    {
        // when
        var result = await _storage.GetJobByNameAsync("nonexistent", AbortToken);

        // then
        result.Should().BeNull();
    }

    [Fact]
    public async Task should_get_all_jobs()
    {
        // given
        await _storage.UpsertJobAsync(_CreateJob("all-1"), AbortToken);
        await _storage.UpsertJobAsync(_CreateJob("all-2"), AbortToken);
        await _storage.UpsertJobAsync(_CreateJob("all-3"), AbortToken);

        // when
        var result = await _storage.GetAllJobsAsync(AbortToken);

        // then
        result.Where(j => j.Name.StartsWith("all-", StringComparison.Ordinal)).Should().HaveCount(3);
    }

    [Fact]
    public async Task should_acquire_due_jobs_and_mark_running()
    {
        // given - create a job with NextRunTime in the past
        var job = _CreateJob("acquire-due");
        job.NextRunTime = DateTimeOffset.UtcNow.AddMinutes(-5);
        await _storage.UpsertJobAsync(job, AbortToken);

        // when
        var acquired = await _storage.AcquireDueJobsAsync(10, "node-1", AbortToken);

        // then
        acquired.Should().HaveCount(1);
        acquired[0].Name.Should().Be("acquire-due");
        acquired[0].Status.Should().Be(ScheduledJobStatus.Running);
        acquired[0].LockHolder.Should().Be("node-1");
        acquired[0].DateLocked.Should().NotBeNull();
    }

    [Fact]
    public async Task should_not_acquire_future_jobs()
    {
        // given - create a job with NextRunTime in the future
        var job = _CreateJob("future-job");
        job.NextRunTime = DateTimeOffset.UtcNow.AddHours(1);
        await _storage.UpsertJobAsync(job, AbortToken);

        // when
        var acquired = await _storage.AcquireDueJobsAsync(10, "node-1", AbortToken);

        // then
        acquired.Where(j => j.Name == "future-job").Should().BeEmpty();
    }

    [Fact]
    public async Task should_not_acquire_disabled_jobs()
    {
        // given
        var job = _CreateJob("disabled-job");
        job.NextRunTime = DateTimeOffset.UtcNow.AddMinutes(-5);
        job.IsEnabled = false;
        await _storage.UpsertJobAsync(job, AbortToken);

        // when
        var acquired = await _storage.AcquireDueJobsAsync(10, "node-1", AbortToken);

        // then
        acquired.Where(j => j.Name == "disabled-job").Should().BeEmpty();
    }

    [Fact]
    public async Task should_skip_locked_jobs()
    {
        // given - create two due jobs
        var job1 = _CreateJob("skip-locked-1");
        job1.NextRunTime = DateTimeOffset.UtcNow.AddMinutes(-5);
        await _storage.UpsertJobAsync(job1, AbortToken);

        var job2 = _CreateJob("skip-locked-2");
        job2.NextRunTime = DateTimeOffset.UtcNow.AddMinutes(-5);
        await _storage.UpsertJobAsync(job2, AbortToken);

        // when - first acquisition gets both, but we simulate concurrent acquisition
        // by acquiring with batch size 1, then acquiring again
        var first = await _storage.AcquireDueJobsAsync(1, "node-1", AbortToken);
        var second = await _storage.AcquireDueJobsAsync(1, "node-2", AbortToken);

        // then - first gets one, second gets the other (already-running jobs skipped)
        first.Should().HaveCount(1);
        second.Should().HaveCount(1);
        first[0].Name.Should().NotBe(second[0].Name);
    }

    [Fact]
    public async Task should_delete_job()
    {
        // given
        var job = _CreateJob("delete-me");
        await _storage.UpsertJobAsync(job, AbortToken);

        // when
        await _storage.DeleteJobAsync(job.Id, AbortToken);

        // then
        var result = await _storage.GetJobByNameAsync("delete-me", AbortToken);
        result.Should().BeNull();
    }

    [Fact]
    public async Task should_delete_job_and_cascade_executions()
    {
        // given
        var job = _CreateJob("cascade-delete");
        await _storage.UpsertJobAsync(job, AbortToken);

        var execution = _CreateExecution(job.Id);
        await _storage.CreateExecutionAsync(execution, AbortToken);

        // when
        await _storage.DeleteJobAsync(job.Id, AbortToken);

        // then - both job and execution should be gone
        var jobResult = await _storage.GetJobByNameAsync("cascade-delete", AbortToken);
        jobResult.Should().BeNull();

        var executions = await _storage.GetExecutionsAsync(job.Id, 10, AbortToken);
        executions.Should().BeEmpty();
    }

    [Fact]
    public async Task should_update_job()
    {
        // given
        var job = _CreateJob("update-me");
        await _storage.UpsertJobAsync(job, AbortToken);

        // when
        job.Status = ScheduledJobStatus.Running;
        job.LockHolder = "node-1";
        job.DateLocked = DateTimeOffset.UtcNow;
        job.LastRunTime = DateTimeOffset.UtcNow;
        await _storage.UpdateJobAsync(job, AbortToken);

        // then
        var result = await _storage.GetJobByNameAsync("update-me", AbortToken);
        result.Should().NotBeNull();
        result!.Status.Should().Be(ScheduledJobStatus.Running);
        result.LockHolder.Should().Be("node-1");
        result.DateLocked.Should().NotBeNull();
        result.LastRunTime.Should().NotBeNull();
    }

    [Fact]
    public async Task should_create_execution()
    {
        // given
        var job = _CreateJob("exec-create");
        await _storage.UpsertJobAsync(job, AbortToken);

        var execution = _CreateExecution(job.Id);

        // when
        await _storage.CreateExecutionAsync(execution, AbortToken);

        // then
        var executions = await _storage.GetExecutionsAsync(job.Id, 10, AbortToken);
        executions.Should().HaveCount(1);
        executions[0].Id.Should().Be(execution.Id);
        executions[0].JobId.Should().Be(job.Id);
        executions[0].Status.Should().Be(JobExecutionStatus.Running);
    }

    [Fact]
    public async Task should_update_execution()
    {
        // given
        var job = _CreateJob("exec-update");
        await _storage.UpsertJobAsync(job, AbortToken);

        var execution = _CreateExecution(job.Id);
        await _storage.CreateExecutionAsync(execution, AbortToken);

        // when
        execution.Status = JobExecutionStatus.Succeeded;
        execution.DateCompleted = DateTimeOffset.UtcNow;
        execution.Duration = 1500;
        await _storage.UpdateExecutionAsync(execution, AbortToken);

        // then
        var executions = await _storage.GetExecutionsAsync(job.Id, 10, AbortToken);
        executions.Should().HaveCount(1);
        executions[0].Status.Should().Be(JobExecutionStatus.Succeeded);
        executions[0].DateCompleted.Should().NotBeNull();
        executions[0].Duration.Should().Be(1500);
    }

    [Fact]
    public async Task should_get_executions_ordered_by_scheduled_time()
    {
        // given
        var job = _CreateJob("exec-order");
        await _storage.UpsertJobAsync(job, AbortToken);

        var now = DateTimeOffset.UtcNow;

        var exec1 = _CreateExecution(job.Id);
        exec1.ScheduledTime = now.AddMinutes(-10);
        await _storage.CreateExecutionAsync(exec1, AbortToken);

        var exec2 = _CreateExecution(job.Id);
        exec2.ScheduledTime = now.AddMinutes(-5);
        await _storage.CreateExecutionAsync(exec2, AbortToken);

        var exec3 = _CreateExecution(job.Id);
        exec3.ScheduledTime = now;
        await _storage.CreateExecutionAsync(exec3, AbortToken);

        // when
        var executions = await _storage.GetExecutionsAsync(job.Id, 10, AbortToken);

        // then - ordered DESC by ScheduledTime
        executions.Should().HaveCount(3);
        executions[0].Id.Should().Be(exec3.Id);
        executions[1].Id.Should().Be(exec2.Id);
        executions[2].Id.Should().Be(exec1.Id);
    }

    [Fact]
    public async Task should_limit_execution_results()
    {
        // given
        var job = _CreateJob("exec-limit");
        await _storage.UpsertJobAsync(job, AbortToken);

        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 5; i++)
        {
            var exec = _CreateExecution(job.Id);
            exec.ScheduledTime = now.AddMinutes(-i);
            await _storage.CreateExecutionAsync(exec, AbortToken);
        }

        // when
        var executions = await _storage.GetExecutionsAsync(job.Id, 3, AbortToken);

        // then
        executions.Should().HaveCount(3);
    }

    [Fact]
    public async Task should_round_trip_nullable_fields()
    {
        // given - job with many nullable fields set
        var job = _CreateJob("nullable-round-trip");
        job.Payload = """{"key":"value"}""";
        job.RetryIntervals = [10, 30, 60];
        job.LastRunTime = DateTimeOffset.UtcNow.AddMinutes(-1);
        job.LastRunDuration = 500;
        job.LockHolder = "test-holder";
        job.DateLocked = DateTimeOffset.UtcNow;
        await _storage.UpsertJobAsync(job, AbortToken);

        // when
        var result = await _storage.GetJobByNameAsync("nullable-round-trip", AbortToken);

        // then
        result.Should().NotBeNull();
        result!.Payload.Should().Be("""{"key":"value"}""");
        result.RetryIntervals.Should().BeEquivalentTo(new[] { 10, 30, 60 });
        result.LastRunTime.Should().NotBeNull();
        result.LastRunDuration.Should().Be(500);
        result.LockHolder.Should().Be("test-holder");
        result.DateLocked.Should().NotBeNull();
    }

    [Fact]
    public async Task should_round_trip_null_optional_fields()
    {
        // given - job with all nullable fields left null
        var job = _CreateJob("null-fields");
        job.CronExpression = null;
        job.Payload = null;
        job.NextRunTime = null;
        job.LastRunTime = null;
        job.LastRunDuration = null;
        job.RetryIntervals = null;
        job.LockHolder = null;
        job.DateLocked = null;
        await _storage.UpsertJobAsync(job, AbortToken);

        // when
        var result = await _storage.GetJobByNameAsync("null-fields", AbortToken);

        // then
        result.Should().NotBeNull();
        result!.CronExpression.Should().BeNull();
        result.Payload.Should().BeNull();
        result.NextRunTime.Should().BeNull();
        result.LastRunTime.Should().BeNull();
        result.LastRunDuration.Should().BeNull();
        result.RetryIntervals.Should().BeNull();
        result.LockHolder.Should().BeNull();
        result.DateLocked.Should().BeNull();
    }

    private static ScheduledJob _CreateJob(string name)
    {
        var now = DateTimeOffset.UtcNow;
        return new ScheduledJob
        {
            Id = Guid.NewGuid(),
            Name = name,
            Type = ScheduledJobType.Recurring,
            CronExpression = "*/5 * * * *",
            TimeZone = "UTC",
            Status = ScheduledJobStatus.Pending,
            NextRunTime = now.AddMinutes(5),
            MaxRetries = 3,
            SkipIfRunning = true,
            IsEnabled = true,
            DateCreated = now,
            DateUpdated = now,
        };
    }

    private static JobExecution _CreateExecution(Guid jobId)
    {
        return new JobExecution
        {
            Id = Guid.NewGuid(),
            JobId = jobId,
            ScheduledTime = DateTimeOffset.UtcNow,
            DateStarted = DateTimeOffset.UtcNow,
            Status = JobExecutionStatus.Running,
            RetryAttempt = 0,
        };
    }
}
