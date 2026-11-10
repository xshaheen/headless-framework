// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Text;
using System.Text.Json;
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
    public async Task should_answer_the_jobs_sidebar_counts_for_every_run_status_in_one_request()
    {
        // given
        await using var app = await JobsDashboardHttpHost.StartAsync(_time, _log, AbortToken);
        var store = app.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
        await store.AddTimeJobsAsync(
            [JobsModuleNode.FailedJob(_time.GetUtcNow()), JobsModuleNode.FailedJob(_time.GetUtcNow())],
            AbortToken
        );
        using var client = JobsDashboardHttpHost.Client(app);
        var cookie = await JobsDashboardHttpHost.SignInAsync(client, "viewer", AbortToken);

        // when
        using var response = await JobsDashboardHttpHost.GetAsync(
            client,
            "/headless/api/counts?product=jobs",
            cookie,
            AbortToken
        );

        // then
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(AbortToken));
        var counts = body.RootElement.GetProperty("counts");
        counts.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(Enum.GetNames<JobStatus>());
        counts.GetProperty("Failed").GetProperty("count").GetInt64().Should().Be(2);
        counts.GetProperty("Failed").GetProperty("capped").GetBoolean().Should().BeFalse();
        counts.GetProperty("Queued").GetProperty("count").GetInt64().Should().Be(0);
    }

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
    public async Task should_create_a_cron_job_with_the_retries_and_intervals_the_operator_chose()
    {
        // given
        await using var app = await JobsDashboardHttpHost.StartAsync(_time, _log, AbortToken);
        using var client = JobsDashboardHttpHost.Client(app);
        var cookie = await JobsDashboardHttpHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var created = await JobsDashboardHttpHost.PostAsync(
            client,
            JobsDashboardHttpHost.Api + "/cron-jobs",
            new
            {
                function = JobsModuleNode.Report,
                expression = "0 0 * * * *",
                retries = 3,
                retryIntervals = new[] { 10, 30, 60 },
            },
            cookie,
            AbortToken
        );

        // then
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await JobsDashboardHttpHost.ReadAsync(created, AbortToken)).GetProperty("id").GetGuid();
        var store = app.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
        var cronJob = await store.GetCronJobByIdAsync(id, AbortToken);
        cronJob!.Retries.Should().Be(3);
        cronJob.RetryIntervals.Should().Equal(10, 30, 60);
    }

    [Fact]
    public async Task should_refuse_a_time_job_for_an_unknown_function_with_the_reason_alone()
    {
        // given
        await using var app = await JobsDashboardHttpHost.StartAsync(_time, _log, AbortToken);
        using var client = JobsDashboardHttpHost.Client(app);
        var cookie = await JobsDashboardHttpHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var refused = await JobsDashboardHttpHost.PostAsync(
            client,
            JobsDashboardHttpHost.Api + "/time-jobs",
            new { function = "no.such" },
            cookie,
            AbortToken
        );

        // then: the operator reads the reason, not the parameter name .NET appends for a developer
        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await JobsDashboardHttpHost.ReadAsync(refused, AbortToken))
            .GetProperty("detail")
            .GetString()
            .Should()
            .Be("No function named 'no.such' is registered.");
        (await JobsDashboardHttpHost.RulesAsync(refused, AbortToken))
            .Should()
            .Equal(new Dictionary<string, string>(StringComparer.Ordinal) { ["function"] = "enum" });
    }

    [Fact]
    public async Task should_refuse_a_time_job_body_naming_each_field_with_the_rule_it_broke()
    {
        // given
        await using var app = await JobsDashboardHttpHost.StartAsync(_time, _log, AbortToken);
        using var client = JobsDashboardHttpHost.Client(app);
        var cookie = await JobsDashboardHttpHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var refused = await JobsDashboardHttpHost.PostAsync(
            client,
            JobsDashboardHttpHost.Api + "/time-jobs",
            new
            {
                function = "",
                retries = 101,
                retryIntervals = new[] { 30, -1 },
            },
            cookie,
            AbortToken
        );

        // then
        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await JobsDashboardHttpHost.RulesAsync(refused, AbortToken))
            .Should()
            .Equal(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["function"] = "required",
                    ["retries"] = "maximum",
                    ["retryIntervals[1]"] = "minimum",
                }
            );
    }

    [Fact]
    public async Task should_name_a_chain_step_without_a_function_by_its_path()
    {
        // given
        await using var app = await JobsDashboardHttpHost.StartAsync(_time, _log, AbortToken);
        using var client = JobsDashboardHttpHost.Client(app);
        var cookie = await JobsDashboardHttpHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var refused = await JobsDashboardHttpHost.PostAsync(
            client,
            JobsDashboardHttpHost.Api + "/time-jobs",
            new { function = JobsModuleNode.Charge, children = new[] { new { description = "no function" } } },
            cookie,
            AbortToken
        );

        // then
        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await JobsDashboardHttpHost.RulesAsync(refused, AbortToken))
            .Should()
            .Equal(new Dictionary<string, string>(StringComparer.Ordinal) { ["children[0].function"] = "required" });
    }

    [Fact]
    public async Task should_name_an_unknown_function_in_a_chain_step_by_its_path()
    {
        // given
        await using var app = await JobsDashboardHttpHost.StartAsync(_time, _log, AbortToken);
        using var client = JobsDashboardHttpHost.Client(app);
        var cookie = await JobsDashboardHttpHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var refused = await JobsDashboardHttpHost.PostAsync(
            client,
            JobsDashboardHttpHost.Api + "/time-jobs",
            new { function = JobsModuleNode.Charge, children = new[] { new { function = "no.such" } } },
            cookie,
            AbortToken
        );

        // then
        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await JobsDashboardHttpHost.RulesAsync(refused, AbortToken))
            .Should()
            .Equal(new Dictionary<string, string>(StringComparer.Ordinal) { ["children[0].function"] = "enum" });
    }

    [Fact]
    public async Task should_refuse_a_chain_step_time_a_daylight_saving_gap_skips_naming_that_step()
    {
        // given: New York's clocks jump from 02:00 to 03:00 on 10 March 2030, so 02:30 never happens there
        await using var app = await JobsDashboardHttpHost.StartAsync(_time, _log, AbortToken);
        using var client = JobsDashboardHttpHost.Client(app);
        var cookie = await JobsDashboardHttpHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var refused = await JobsDashboardHttpHost.PostAsync(
            client,
            JobsDashboardHttpHost.Api + "/time-jobs",
            new
            {
                function = JobsModuleNode.Charge,
                executionTime = "2030-03-10T01:30:00",
                timeZoneId = "America/New_York",
                children = new[] { new { function = JobsModuleNode.Charge, executionTime = "2030-03-10T02:30:00" } },
            },
            cookie,
            AbortToken
        );

        // then: the refusal names the skipped time, not the request the conversion would otherwise have blamed
        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await JobsDashboardHttpHost.RulesAsync(refused, AbortToken))
            .Should()
            .Equal(
                new Dictionary<string, string>(StringComparer.Ordinal) { ["children[0].executionTime"] = "invalid" }
            );
        (await JobsDashboardHttpHost.ReadAsync(refused, AbortToken))
            .GetProperty("detail")
            .GetString()
            .Should()
            .Be(
                "2030-03-10 02:30 does not exist in America/New_York: the clocks skip it for daylight saving. Choose a time outside the gap."
            );
    }

    [Theory]
    [InlineData("/time-jobs")]
    [InlineData("/cron-jobs")]
    public async Task should_refuse_a_create_body_that_is_not_json_as_the_body_breaking_the_json_rule(string route)
    {
        // given
        await using var app = await JobsDashboardHttpHost.StartAsync(_time, _log, AbortToken);
        using var client = JobsDashboardHttpHost.Client(app);
        var cookie = await JobsDashboardHttpHost.SignInAsync(client, "ops", AbortToken);

        using var malformed = new StringContent("{ not json", Encoding.UTF8, "application/json");

        // when
        using var refused = await JobsDashboardHttpHost.PostAsync(
            client,
            JobsDashboardHttpHost.Api + route,
            malformed,
            cookie,
            AbortToken
        );

        // then: the same rule the shell's commands and a peer node name a malformed body by
        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await JobsDashboardHttpHost.RulesAsync(refused, AbortToken))
            .Should()
            .Equal(new Dictionary<string, string>(StringComparer.Ordinal) { ["body"] = "json" });
    }

    [Theory]
    [InlineData("""{ "function": "report.build" }""", "expression", "required")]
    [InlineData("""{ "function": "report.build", "expression": "not cron" }""", "expression", "format")]
    [InlineData(
        """{ "function": "report.build", "expression": "0 0 * * * *", "timeZoneId": "Mars/Olympus" }""",
        "timeZoneId",
        "enum"
    )]
    [InlineData("""{ "expression": "0 0 * * * *", "retries": -1 }""", "function", "required")]
    public async Task should_refuse_a_cron_job_body_naming_the_field_and_rule(string json, string field, string rule)
    {
        // given
        await using var app = await JobsDashboardHttpHost.StartAsync(_time, _log, AbortToken);
        using var client = JobsDashboardHttpHost.Client(app);
        var cookie = await JobsDashboardHttpHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var refused = await JobsDashboardHttpHost.PostAsync(
            client,
            JobsDashboardHttpHost.Api + "/cron-jobs",
            JsonDocument.Parse(json).RootElement,
            cookie,
            AbortToken
        );

        // then
        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await JobsDashboardHttpHost.RulesAsync(refused, AbortToken))
            .Should()
            .ContainKey(field)
            .WhoseValue.Should()
            .Be(rule);
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
