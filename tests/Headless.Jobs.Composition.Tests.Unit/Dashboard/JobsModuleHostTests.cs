// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Jobs;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Dashboard;

/// <summary>
/// The Jobs module registered with <c>AddJobs()</c> on a real host: the shell's command endpoints enforce the operate
/// policy before the module runs, a refused call answers <c>409</c> with the module's reason, the create endpoints
/// need operate, and a host without Headless Jobs fails to start naming the missing call.
/// </summary>
public sealed class JobsModuleHostTests : TestBase
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero));
    private readonly RecordingOperatorLog _log = new();

    [Fact]
    public async Task should_answer_403_and_log_nothing_when_a_viewer_posts_requeue()
    {
        // given
        await using var app = await JobsDashboardHttpHost.StartAsync(_time, _log, AbortToken);
        var store = app.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
        var job = JobsModuleNode.FailedJob(_time.GetUtcNow());
        await store.AddTimeJobsAsync([job], AbortToken);
        using var client = JobsDashboardHttpHost.Client(app);
        var cookie = await JobsDashboardHttpHost.SignInAsync(client, "viewer", AbortToken);

        // when
        using var response = await JobsDashboardHttpHost.PostAsync(
            client,
            JobsDashboardHttpHost.Commands + "/jobs.requeue",
            new
            {
                ids = new[] { job.Id.ToString() },
                reason = "retry",
                confirmed = true,
            },
            cookie,
            AbortToken
        );

        // then
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _log.Entries.Should().BeEmpty();
        (await store.GetJobRunAsync(job.Id, AbortToken))!.Status.Should().Be(JobStatus.Failed);
    }

    [Fact]
    public async Task should_answer_409_with_resume_first_when_run_now_is_posted_on_a_paused_cron_job()
    {
        // given
        await using var app = await JobsDashboardHttpHost.StartAsync(_time, _log, AbortToken);
        var store = app.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
        var cronJob = JobsModuleNode.CronJob(paused: true);
        await store.InsertCronJobsAsync([cronJob], AbortToken);
        using var client = JobsDashboardHttpHost.Client(app);
        var cookie = await JobsDashboardHttpHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var response = await JobsDashboardHttpHost.PostAsync(
            client,
            JobsDashboardHttpHost.Commands + "/jobs.run-now",
            new { ids = new[] { cronJob.Id.ToString() } },
            cookie,
            AbortToken
        );

        // then
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await JobsDashboardHttpHost.ReadAsync(response, AbortToken))
            .GetProperty("detail")
            .GetString()
            .Should()
            .Be("Paused — resume first");
        _log.Entries.Should().BeEmpty("a refused command writes no operator-log entry");
    }

    [Fact]
    public async Task should_create_a_time_job_for_an_operator_and_refuse_a_viewer()
    {
        // given
        await using var app = await JobsDashboardHttpHost.StartAsync(_time, _log, AbortToken);
        using var client = JobsDashboardHttpHost.Client(app);
        var viewer = await JobsDashboardHttpHost.SignInAsync(client, "viewer", AbortToken);
        var operatorCookie = await JobsDashboardHttpHost.SignInAsync(client, "ops", AbortToken);
        var body = new { function = JobsModuleNode.Charge, description = "from the dashboard" };

        // when
        using var refused = await JobsDashboardHttpHost.PostAsync(
            client,
            JobsDashboardHttpHost.Api + "/time-jobs",
            body,
            viewer,
            AbortToken
        );
        using var created = await JobsDashboardHttpHost.PostAsync(
            client,
            JobsDashboardHttpHost.Api + "/time-jobs",
            body,
            operatorCookie,
            AbortToken
        );

        // then
        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await JobsDashboardHttpHost.ReadAsync(created, AbortToken)).GetProperty("id").GetGuid();
        var store = app.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
        (await store.GetJobRunAsync(id, AbortToken))!.Description.Should().Be("from the dashboard");
    }

    [Fact]
    public async Task should_fail_at_startup_naming_add_headless_jobs_when_jobs_is_not_registered()
    {
        // when
        var start = () => JobsDashboardHttpHost.StartAsync(_time, _log, AbortToken, addJobs: false);

        // then
        (await start.Should().ThrowAsync<Exception>())
            .Which.Message.Should()
            .Contain("AddHeadlessJobs");
    }
}
