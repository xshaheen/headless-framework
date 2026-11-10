// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Security.Claims;
using Headless.Dashboard;
using Headless.Jobs;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Dashboard;

/// <summary>
/// Edit, Duplicate, and Force delete on a time job and Delete on a cron job, as the shell calls them: each answers from
/// one rule set, applies through a write the store checks again in the same commit, and refuses with the store's
/// outcome when the row moved after the allowed-actions read.
/// </summary>
public sealed class JobsModuleTimeJobCommandsTests : TestBase
{
    private static readonly ClaimsPrincipal _User = new(new ClaimsIdentity("test"));
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero));
    private readonly RecordingOperatorLog _log = new();

    [Fact]
    public async Task should_force_delete_a_running_job_with_cancel_requested_and_log_the_reason()
    {
        // given
        await using var app = await JobsDashboardHttpHost.StartAsync(_time, _log, AbortToken);
        var store = app.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
        var (job, _) = await _StartAsync(store, cancel: true);
        using var client = JobsDashboardHttpHost.Client(app);
        var cookie = await JobsDashboardHttpHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var response = await JobsDashboardHttpHost.PostAsync(
            client,
            JobsDashboardHttpHost.Commands + "/jobs.force-delete",
            new
            {
                ids = new[] { job.ToString() },
                reason = "worker hung on a dead socket",
                confirmed = true,
            },
            cookie,
            AbortToken
        );

        // then
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await store.GetJobRunAsync(job, AbortToken)).Should().BeNull();
        _log.Entries.Should()
            .Contain(entry =>
                entry.CommandId == "jobs.force-delete"
                && entry.Reason == "worker hung on a dead socket"
                && entry.Ids.Contains(job.ToString())
            );
    }

    [Fact]
    public async Task should_refuse_force_delete_with_cancel_first_when_no_cancel_was_requested()
    {
        // given
        await using var node = JobsModuleNode.Create(_time);
        var (job, _) = await _StartAsync(node.Store, cancel: false);

        // when
        var allowed = await node.Module.GetAllowedCommandsAsync(_Allowed(JobsViews.RunKind, job), AbortToken);
        var result = await node.Module.ExecuteAsync(_Request("jobs.force-delete", job), AbortToken);

        // then
        _Reason(allowed, "jobs.force-delete").Should().Be("Cancel first");
        result.Refusal.Should().Be("Cancel first");
        (await node.Store.GetJobRunAsync(job, AbortToken))!.Status.Should().Be(JobStatus.InProgress);
    }

    [Fact]
    public async Task should_refuse_a_force_delete_with_the_stores_outcome_when_the_job_finished_after_the_read()
    {
        // given: the job is running with cancel requested when the rules read it, and finishes before the delete lands
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity>? store = null;
        string? owner = null;
        Guid job = default;
        await using var node = JobsModuleNode.Create(
            _time,
            backend: inner =>
                InterceptedJobsDashboardBackend.Wrap(
                    inner,
                    (method, _, proceed) =>
                    {
                        if (
                            string.Equals(
                                method,
                                nameof(IJobsDashboardBackend.ForceDeleteTimeJobAsync),
                                StringComparison.Ordinal
                            )
                        )
                        {
                            _CompleteAsync(store!, job, owner!).GetAwaiter().GetResult();
                        }

                        return proceed();
                    }
                )
        );
        store = node.Store;
        (job, owner) = await _StartAsync(store, cancel: true);
        var allowed = await node.Module.GetAllowedCommandsAsync(_Allowed(JobsViews.RunKind, job), AbortToken);
        _Answer(allowed, "jobs.force-delete").Allowed.Should().BeTrue();

        // when
        var result = await node.Module.ExecuteAsync(_Request("jobs.force-delete", job), AbortToken);

        // then
        result.Refusal.Should().Be("Not running — delete it instead");
        (await store.GetJobRunAsync(job, AbortToken))!.Status.Should().Be(JobStatus.Succeeded);
        node.Published.For(job).Should().BeEmpty("nothing was removed");
    }

    [Fact]
    public async Task should_refuse_a_cron_delete_in_place_while_an_occurrence_is_running()
    {
        // given
        await using var node = JobsModuleNode.Create(_time);
        var cronJob = JobsModuleNode.CronJob();
        await node.Store.InsertCronJobsAsync([cronJob], AbortToken);
        await _AddOccurrencesAsync(node.Store, cronJob.Id, JobStatus.InProgress, 1);

        // when
        var allowed = await node.Module.GetAllowedCommandsAsync(
            _Allowed(JobsViews.CronJobKind, cronJob.Id),
            AbortToken
        );
        var result = await node.Module.ExecuteAsync(_Request("jobs.delete-cron", cronJob.Id), AbortToken);

        // then
        _Reason(allowed, "jobs.delete-cron").Should().Be("Running — wait or cancel the occurrence");
        result.Refusal.Should().Be("Running — wait or cancel the occurrence");
        (await node.Store.GetCronJobByIdAsync(cronJob.Id, AbortToken)).Should().NotBeNull();
    }

    [Fact]
    public async Task should_delete_a_cron_job_with_its_twelve_stored_occurrences_and_report_twelve()
    {
        // given
        await using var node = JobsModuleNode.Create(_time);
        var cronJob = JobsModuleNode.CronJob();
        await node.Store.InsertCronJobsAsync([cronJob], AbortToken);
        await _AddOccurrencesAsync(node.Store, cronJob.Id, JobStatus.Succeeded, 8);
        await _AddOccurrencesAsync(node.Store, cronJob.Id, JobStatus.Failed, 4);

        // when
        var result = await node.Module.ExecuteAsync(_Request("jobs.delete-cron", cronJob.Id), AbortToken);

        // then
        var row = result.Rows.Should().ContainSingle().Subject;
        row.Outcome.Should().Be(DashboardRowOutcome.Done);
        row.Reason.Should().Be("12 stored occurrences removed");
        (await node.Store.GetCronJobByIdAsync(cronJob.Id, AbortToken)).Should().BeNull();
        (await node.Store.GetAllCronJobOccurrencesAsync(x => x.CronJobId == cronJob.Id, AbortToken)).Should().BeEmpty();
        node.Published.For(cronJob.Id).Should().ContainSingle();
    }

    [Fact]
    public async Task should_refuse_a_cron_delete_when_an_occurrence_started_after_the_read_and_keep_everything()
    {
        // given: no occurrence runs when the rules read the job, and one starts before the delete lands
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity>? store = null;
        var cronJob = JobsModuleNode.CronJob();
        await using var node = JobsModuleNode.Create(
            _time,
            backend: inner =>
                InterceptedJobsDashboardBackend.Wrap(
                    inner,
                    (method, _, proceed) =>
                    {
                        if (
                            string.Equals(
                                method,
                                nameof(IJobsDashboardBackend.DeleteCronJobAsync),
                                StringComparison.Ordinal
                            )
                        )
                        {
                            _AddOccurrencesAsync(store!, cronJob.Id, JobStatus.InProgress, 1).GetAwaiter().GetResult();
                        }

                        return proceed();
                    }
                )
        );
        store = node.Store;
        await store.InsertCronJobsAsync([cronJob], AbortToken);
        await _AddOccurrencesAsync(store, cronJob.Id, JobStatus.Succeeded, 3);
        var allowed = await node.Module.GetAllowedCommandsAsync(
            _Allowed(JobsViews.CronJobKind, cronJob.Id),
            AbortToken
        );
        _Answer(allowed, "jobs.delete-cron").Allowed.Should().BeTrue();

        // when
        var result = await node.Module.ExecuteAsync(_Request("jobs.delete-cron", cronJob.Id), AbortToken);

        // then
        result.Refusal.Should().Be("Running — wait or cancel the occurrence");
        (await store.GetCronJobByIdAsync(cronJob.Id, AbortToken)).Should().NotBeNull();
        (await store.GetAllCronJobOccurrencesAsync(x => x.CronJobId == cronJob.Id, AbortToken)).Should().HaveCount(4);
    }

    [Fact]
    public async Task should_refuse_deleting_a_cron_job_defined_in_code_and_allow_a_retired_one()
    {
        // given
        await using var node = JobsModuleNode.Create(_time);
        var seeded = JobsModuleNode.CronJob();
        seeded.InitIdentifier = "headless.jobs.seed:default:cron:" + JobsModuleNode.Report;
        var retired = JobsModuleNode.CronJob();
        retired.InitIdentifier = "headless.jobs.seed:default:retired:cron:old.report";
        await node.Store.InsertCronJobsAsync([seeded, retired], AbortToken);

        // when
        var seededAnswer = await node.Module.GetAllowedCommandsAsync(
            _Allowed(JobsViews.CronJobKind, seeded.Id),
            AbortToken
        );
        var retiredAnswer = await node.Module.GetAllowedCommandsAsync(
            _Allowed(JobsViews.CronJobKind, retired.Id),
            AbortToken
        );

        // then
        _Reason(seededAnswer, "jobs.delete-cron").Should().Be("Defined in code");
        _Answer(retiredAnswer, "jobs.delete-cron").Allowed.Should().BeTrue();
    }

    [Fact]
    public async Task should_duplicate_a_finished_standalone_job_with_its_request_and_retries_and_no_business_key()
    {
        // given
        await using var node = JobsModuleNode.Create(_time);
        var source = JobsModuleNode.FailedJob(_time.GetUtcNow());
        source.Request = [7, 8, 9];
        source.Retries = 3;
        source.RetryIntervals = [30, 60, 300];
        source.Description = "Monthly charge";
        await node.Store.AddTimeJobsAsync([source], AbortToken);

        // when
        var result = await node.Module.ExecuteAsync(
            _Request(
                "jobs.duplicate",
                source.Id,
                new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    ["executionTime"] = JsonSerializer.SerializeToElement("2026-11-05T13:00:00Z"),
                }
            ),
            AbortToken
        );

        // then
        result.Refusal.Should().BeNull();
        var row = result.Rows.Should().ContainSingle().Subject;
        row.Outcome.Should().Be(DashboardRowOutcome.Done);
        row.Created.Should().NotBeNull();
        row.Created!.Kind.Should().Be(JobsViews.RunKind);
        var copyId = Guid.Parse(row.Created.Id);
        copyId.Should().NotBe(source.Id);
        var copy = (await node.Store.GetTimeJobByIdAsync(copyId, AbortToken))!;
        copy.Function.Should().Be(source.Function);
        copy.Request.Should().Equal(7, 8, 9);
        copy.Retries.Should().Be(3);
        copy.RetryIntervals.Should().Equal(30, 60, 300);
        copy.Description.Should().Be("Monthly charge");
        copy.BusinessKey.Should().BeNull();
        copy.ParentId.Should().BeNull();
        copy.Status.Should().Be(JobStatus.Idle);
        copy.ExecutionTime.Should().Be(new DateTime(2026, 11, 5, 13, 0, 0, DateTimeKind.Utc));
        (await node.Store.GetJobRunAsync(source.Id, AbortToken))!.Status.Should().Be(JobStatus.Failed);
        node.Published.For(copyId).Should().NotBeEmpty();
    }

    [Fact]
    public async Task should_duplicate_a_job_to_run_now_when_no_execution_time_is_given()
    {
        // given
        await using var node = JobsModuleNode.Create(_time);
        var source = JobsModuleNode.FailedJob(_time.GetUtcNow());
        await node.Store.AddTimeJobsAsync([source], AbortToken);

        // when
        var result = await node.Module.ExecuteAsync(_Request("jobs.duplicate", source.Id), AbortToken);

        // then
        var created = result.Rows.Should().ContainSingle().Subject.Created;
        created.Should().NotBeNull();
        var copy = (await node.Store.GetTimeJobByIdAsync(Guid.Parse(created!.Id), AbortToken))!;
        copy.ExecutionTime.Should().NotBeNull("a run with no due time is never claimed");
        copy.ExecutionTime!.Value.Should().BeOnOrBefore(_time.GetUtcNow().UtcDateTime);
    }

    [Fact]
    public async Task should_refuse_duplicating_a_chain_step()
    {
        // given
        await using var node = JobsModuleNode.Create(_time);
        var root = JobsModuleNode.FailedJob(_time.GetUtcNow());
        var step = JobsModuleNode.FailedJob(_time.GetUtcNow());
        step.ExecutionTime = null;
        step.RunCondition = RunCondition.OnSuccess;
        root.Children.Add(step);
        await node.Store.AddTimeJobsAsync([root], AbortToken);

        // when
        var allowed = await node.Module.GetAllowedCommandsAsync(_Allowed(JobsViews.RunKind, step.Id), AbortToken);
        var result = await node.Module.ExecuteAsync(_Request("jobs.duplicate", step.Id), AbortToken);

        // then
        _Reason(allowed, "jobs.duplicate").Should().Be("Part of a chain — chains are not duplicated");
        result.Refusal.Should().Be("Part of a chain — chains are not duplicated");
        (
            await node.Store.CountJobRunsAsync(
                new JobRunQuery { Kind = JobRunKind.TimeJob },
                cancellationToken: AbortToken
            )
        )
            .Count.Should()
            .Be(2, "a refused duplicate adds no run");
    }

    [Fact]
    public async Task should_refuse_editing_a_running_job()
    {
        // given
        await using var node = JobsModuleNode.Create(_time);
        var (job, _) = await _StartAsync(node.Store, cancel: false);

        // when
        var allowed = await node.Module.GetAllowedCommandsAsync(_Allowed(JobsViews.RunKind, job), AbortToken);
        var result = await node.Module.ExecuteAsync(_Edit(job, "2026-11-05T15:00:00Z"), AbortToken);

        // then
        _Reason(allowed, "jobs.edit").Should().Be("Running — it can no longer be edited");
        result.Refusal.Should().Be("Running — it can no longer be edited");
    }

    [Fact]
    public async Task should_edit_the_execution_time_of_a_queued_job_and_keep_its_other_fields()
    {
        // given
        await using var node = JobsModuleNode.Create(_time);
        var job = JobsModuleNode.IdleJob(_time.GetUtcNow());
        job.Status = JobStatus.Queued;
        job.ExecutionTime = _time.GetUtcNow().UtcDateTime.AddHours(1);
        job.Request = [1, 2, 3];
        job.Retries = 2;
        await node.Store.AddTimeJobsAsync([job], AbortToken);

        // when
        var allowed = await node.Module.GetAllowedCommandsAsync(_Allowed(JobsViews.RunKind, job.Id), AbortToken);
        var result = await node.Module.ExecuteAsync(_Edit(job.Id, "2026-11-05T15:30:00+02:00"), AbortToken);

        // then
        _Answer(allowed, "jobs.edit").Allowed.Should().BeTrue();
        result.Refusal.Should().BeNull();
        result.Rows.Should().ContainSingle().Which.Outcome.Should().Be(DashboardRowOutcome.Done);
        var stored = (await node.Store.GetTimeJobByIdAsync(job.Id, AbortToken))!;
        stored.ExecutionTime.Should().Be(new DateTime(2026, 11, 5, 13, 30, 0, DateTimeKind.Utc));
        stored.Request.Should().Equal(1, 2, 3);
        stored.Retries.Should().Be(2);
        node.Published.For(job.Id).Should().NotBeEmpty();
    }

    [Fact]
    public async Task should_fail_an_edit_whose_retry_intervals_are_not_whole_seconds()
    {
        // given
        await using var node = JobsModuleNode.Create(_time);
        var job = JobsModuleNode.IdleJob(_time.GetUtcNow());
        await node.Store.AddTimeJobsAsync([job], AbortToken);

        // when
        var result = await node.Module.ExecuteAsync(
            _Request(
                "jobs.edit",
                job.Id,
                new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    ["retryIntervals"] = JsonSerializer.SerializeToElement("[30, -5]"),
                }
            ),
            AbortToken
        );

        // then
        result.Rows.Should().ContainSingle().Which.Outcome.Should().Be(DashboardRowOutcome.Failed);
        result.Refusal.Should().StartWith("Retry intervals must be a JSON array of whole seconds");
    }

    [Fact]
    public async Task should_show_a_viewer_all_four_actions_disabled_with_needs_operate_permission()
    {
        // given
        await using var app = await JobsDashboardHttpHost.StartAsync(_time, _log, AbortToken);
        var store = app.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
        var job = JobsModuleNode.FailedJob(_time.GetUtcNow());
        await store.AddTimeJobsAsync([job], AbortToken);
        var cronJob = JobsModuleNode.CronJob();
        await store.InsertCronJobsAsync([cronJob], AbortToken);
        using var client = JobsDashboardHttpHost.Client(app);
        var cookie = await JobsDashboardHttpHost.SignInAsync(client, "viewer", AbortToken);

        // when
        var run = await _AllowedOverHttpAsync(client, cookie, JobsViews.RunKind, job.Id);
        var cron = await _AllowedOverHttpAsync(client, cookie, JobsViews.CronJobKind, cronJob.Id);

        // then
        var commands = run.Concat(cron)
            .Where(command =>
                command.GetProperty("id").GetString()
                    is "jobs.edit"
                        or "jobs.duplicate"
                        or "jobs.force-delete"
                        or "jobs.delete-cron"
            )
            .ToArray();
        commands.Should().HaveCount(4);
        commands.Should().OnlyContain(command => !command.GetProperty("allowed").GetBoolean());
        commands
            .Select(command => command.GetProperty("disabledReason").GetString())
            .Should()
            .AllBe("Needs operate permission");
    }

    private static async Task<JsonElement[]> _AllowedOverHttpAsync(
        HttpClient client,
        string cookie,
        string kind,
        Guid id
    )
    {
        using var response = await JobsDashboardHttpHost.GetAsync(
            client,
            $"{JobsDashboardHttpHost.Commands}/allowed?kind={kind}&id={id}",
            cookie,
            AbortToken
        );
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return
        [
            .. (await JobsDashboardHttpHost.ReadAsync(response, AbortToken))
                .GetProperty("objects")[0]
                .GetProperty("commands")
                .EnumerateArray(),
        ];
    }

    private async Task<(Guid Job, string Owner)> _StartAsync(
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> store,
        bool cancel
    )
    {
        var job = JobsModuleNode.IdleJob(_time.GetUtcNow());
        await store.AddTimeJobsAsync([job], AbortToken);
        var claimed = (await store.AcquireImmediateTimeJobsAsync([job.Id], AbortToken))
            .Should()
            .ContainSingle()
            .Subject;

        if (cancel)
        {
            (await store.RequestTimeJobCancellationAsync(job.Id, AbortToken)).Should().BeTrue();
        }

        (await store.GetJobRunAsync(job.Id, AbortToken))!.Status.Should().Be(JobStatus.InProgress);

        return (job.Id, claimed.OwnerId!);
    }

    private static async Task _CompleteAsync(
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> store,
        Guid job,
        string owner
    )
    {
        var completion = new JobExecutionState
        {
            FunctionName = JobsModuleNode.Charge,
            JobId = job,
            ClaimOwner = owner,
        }.SetProperty(x => x.Status, JobStatus.Succeeded);

        (await store.UpdateTimeJobAsync(completion, AbortToken)).Should().Be(1);
    }

    private async Task _AddOccurrencesAsync(
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> store,
        Guid cronJobId,
        JobStatus status,
        int count
    )
    {
        var now = _time.GetUtcNow();
        var occurrences = Enumerable
            .Range(0, count)
            .Select(index => new CronJobOccurrenceEntity<CronJobEntity>
            {
                Id = Guid.NewGuid(),
                CronJobId = cronJobId,
                Status = status,
                ExecutionTime = now.UtcDateTime.AddHours(-index - 1),
                ExecutedAt = status is JobStatus.Succeeded or JobStatus.Failed ? now.AddHours(-index - 1) : null,
                CreatedAt = now.AddDays(-1),
                UpdatedAt = now.AddDays(-1),
            })
            .ToArray();

        (await store.InsertCronJobOccurrencesAsync(occurrences, AbortToken)).Should().Be(count);
    }

    private static DashboardCommandAvailability _Answer(
        IReadOnlyList<DashboardCommandAvailability> answers,
        string commandId
    ) => answers.Single(answer => string.Equals(answer.CommandId, commandId, StringComparison.Ordinal));

    private static string? _Reason(IReadOnlyList<DashboardCommandAvailability> answers, string commandId) =>
        _Answer(answers, commandId).DisabledReason;

    private static DashboardCommandRequest _Edit(Guid id, string executionTime) =>
        _Request(
            "jobs.edit",
            id,
            new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["executionTime"] = JsonSerializer.SerializeToElement(executionTime),
            }
        );

    private static DashboardAllowedCommandsRequest _Allowed(string kind, Guid id) =>
        new()
        {
            Targets =
            [
                new ObjectRef
                {
                    Module = "jobs",
                    Kind = kind,
                    Id = id.ToString(),
                },
            ],
            User = _User,
        };

    private static DashboardCommandRequest _Request(
        string commandId,
        Guid id,
        IReadOnlyDictionary<string, JsonElement>? arguments = null
    ) =>
        new()
        {
            CommandId = commandId,
            Ids = [id.ToString()],
            Arguments = arguments ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal),
            Channel = DashboardChannels.Ui,
            User = _User,
            Reason = "test",
        };
}
