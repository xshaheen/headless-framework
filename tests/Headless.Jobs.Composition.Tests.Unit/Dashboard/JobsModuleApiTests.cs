// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Jobs;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Dashboard;

/// <summary>
/// The Jobs module's read API over HTTP on a real host: each route answers its view, a bad kind, status, or cursor
/// answers <c>400</c>, the list and fire counts are clamped, and an id that is not a GUID or names nothing answers
/// <c>404</c>.
/// </summary>
public sealed class JobsModuleApiTests : TestBase
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero));
    private readonly RecordingOperatorLog _log = new();

    [Fact]
    public async Task should_list_the_runs_matching_a_status()
    {
        // given
        await using var app = await JobsDashboardHttpHost.StartAsync(_time, _log, AbortToken);
        var failed = JobsModuleNode.FailedJob(_time.GetUtcNow());
        await _Store(app).AddTimeJobsAsync([failed, JobsModuleNode.IdleJob(_time.GetUtcNow())], AbortToken);
        using var client = JobsDashboardHttpHost.Client(app);
        var cookie = await JobsDashboardHttpHost.SignInAsync(client, "viewer", AbortToken);

        // when
        using var response = await JobsDashboardHttpHost.GetAsync(
            client,
            JobsDashboardHttpHost.Api + "/runs?status=Failed",
            cookie,
            AbortToken
        );

        // then
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await JobsDashboardHttpHost.ReadAsync(response, AbortToken);
        body.GetProperty("items")
            .EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid())
            .Should()
            .Equal(failed.Id);
        body.GetProperty("count").GetInt32().Should().Be(1);
    }

    [Theory]
    [InlineData("kind=Recurring")]
    [InlineData("status=Exploded")]
    [InlineData("after=not-a-cursor")]
    public async Task should_answer_400_for_a_runs_query_it_cannot_read(string query)
    {
        // given
        await using var app = await JobsDashboardHttpHost.StartAsync(_time, _log, AbortToken);
        using var client = JobsDashboardHttpHost.Client(app);
        var cookie = await JobsDashboardHttpHost.SignInAsync(client, "viewer", AbortToken);

        // when
        using var response = await JobsDashboardHttpHost.GetAsync(
            client,
            JobsDashboardHttpHost.Api + "/runs?" + query,
            cookie,
            AbortToken
        );

        // then
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1_000, 200)]
    public async Task should_clamp_the_runs_page_size_between_1_and_200(int limit, int expected)
    {
        // given
        await using var app = await JobsDashboardHttpHost.StartAsync(_time, _log, AbortToken);
        var jobs = Enumerable.Range(0, 201).Select(_ => JobsModuleNode.IdleJob(_time.GetUtcNow())).ToArray();
        await _Store(app).AddTimeJobsAsync(jobs, AbortToken);
        using var client = JobsDashboardHttpHost.Client(app);
        var cookie = await JobsDashboardHttpHost.SignInAsync(client, "viewer", AbortToken);

        // when
        using var response = await JobsDashboardHttpHost.GetAsync(
            client,
            JobsDashboardHttpHost.Api + "/runs?limit=" + limit.ToString(CultureInfo.InvariantCulture),
            cookie,
            AbortToken
        );

        // then
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await JobsDashboardHttpHost.ReadAsync(response, AbortToken);
        body.GetProperty("items").GetArrayLength().Should().Be(expected);
        body.GetProperty("count").GetInt32().Should().Be(201);
        body.GetProperty("nextCursor").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task should_answer_a_run_and_its_attempts()
    {
        // given
        await using var app = await JobsDashboardHttpHost.StartAsync(_time, _log, AbortToken);
        var job = JobsModuleNode.FailedJob(_time.GetUtcNow());
        await _Store(app).AddTimeJobsAsync([job], AbortToken);
        using var client = JobsDashboardHttpHost.Client(app);
        var cookie = await JobsDashboardHttpHost.SignInAsync(client, "viewer", AbortToken);

        // when
        using var run = await JobsDashboardHttpHost.GetAsync(
            client,
            JobsDashboardHttpHost.Api + "/runs/" + job.Id,
            cookie,
            AbortToken
        );
        using var attempts = await JobsDashboardHttpHost.GetAsync(
            client,
            JobsDashboardHttpHost.Api + "/runs/" + job.Id + "/attempts",
            cookie,
            AbortToken
        );

        // then
        run.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = await JobsDashboardHttpHost.ReadAsync(run, AbortToken);
        detail.GetProperty("run").GetProperty("id").GetGuid().Should().Be(job.Id);
        detail.GetProperty("run").GetProperty("status").GetString().Should().Be("Failed");
        attempts.StatusCode.Should().Be(HttpStatusCode.OK);
        (await JobsDashboardHttpHost.ReadAsync(attempts, AbortToken))
            .GetProperty("items")
            .ValueKind.Should()
            .Be(JsonValueKind.Array);
    }

    [Theory]
    [InlineData("/runs/not-a-guid")]
    [InlineData("/runs/00000000-0000-0000-0000-00000000abcd")]
    [InlineData("/runs/not-a-guid/attempts")]
    [InlineData("/runs/00000000-0000-0000-0000-00000000abcd/attempts")]
    [InlineData("/cron-jobs/not-a-guid")]
    [InlineData("/cron-jobs/00000000-0000-0000-0000-00000000abcd")]
    public async Task should_answer_404_for_an_id_that_is_not_a_guid_or_names_nothing(string path)
    {
        // given
        await using var app = await JobsDashboardHttpHost.StartAsync(_time, _log, AbortToken);
        using var client = JobsDashboardHttpHost.Client(app);
        var cookie = await JobsDashboardHttpHost.SignInAsync(client, "viewer", AbortToken);

        // when
        using var response = await JobsDashboardHttpHost.GetAsync(
            client,
            JobsDashboardHttpHost.Api + path,
            cookie,
            AbortToken
        );

        // then
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task should_list_cron_jobs_and_answer_one_with_its_next_fires()
    {
        // given
        await using var app = await JobsDashboardHttpHost.StartAsync(_time, _log, AbortToken);
        var cronJob = JobsModuleNode.CronJob();
        await _Store(app).InsertCronJobsAsync([cronJob], AbortToken);
        using var client = JobsDashboardHttpHost.Client(app);
        var cookie = await JobsDashboardHttpHost.SignInAsync(client, "viewer", AbortToken);

        // when
        using var list = await JobsDashboardHttpHost.GetAsync(
            client,
            JobsDashboardHttpHost.Api + "/cron-jobs",
            cookie,
            AbortToken
        );
        using var one = await JobsDashboardHttpHost.GetAsync(
            client,
            JobsDashboardHttpHost.Api + "/cron-jobs/" + cronJob.Id + "?fires=3",
            cookie,
            AbortToken
        );

        // then
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        (await JobsDashboardHttpHost.ReadAsync(list, AbortToken))
            .GetProperty("items")
            .EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid())
            .Should()
            .Equal(cronJob.Id);
        one.StatusCode.Should().Be(HttpStatusCode.OK);
        var view = await JobsDashboardHttpHost.ReadAsync(one, AbortToken);
        view.GetProperty("expression").GetString().Should().Be("0 0 * * * *");
        view.GetProperty("nextFires").GetArrayLength().Should().Be(3);
    }

    [Fact]
    public async Task should_list_each_active_cron_jobs_next_fires_when_the_list_asks_for_them()
    {
        // given
        await using var app = await JobsDashboardHttpHost.StartAsync(_time, _log, AbortToken);
        var active = JobsModuleNode.CronJob();
        var paused = JobsModuleNode.CronJob(paused: true);
        await _Store(app).InsertCronJobsAsync([active, paused], AbortToken);
        using var client = JobsDashboardHttpHost.Client(app);
        var cookie = await JobsDashboardHttpHost.SignInAsync(client, "viewer", AbortToken);

        // when
        using var list = await JobsDashboardHttpHost.GetAsync(
            client,
            JobsDashboardHttpHost.Api + "/cron-jobs?fires=4",
            cookie,
            AbortToken
        );

        // then: a paused cron job fires nothing, so it lists no fires
        var items = (await JobsDashboardHttpHost.ReadAsync(list, AbortToken))
            .GetProperty("items")
            .EnumerateArray()
            .ToList();
        items
            .ToDictionary(
                item => item.GetProperty("id").GetGuid(),
                item => item.GetProperty("nextFires").GetArrayLength()
            )
            .Should()
            .BeEquivalentTo(new Dictionary<Guid, int> { [active.Id] = 4, [paused.Id] = 0 });
    }

    [Fact]
    public async Task should_answer_400_for_a_preview_with_no_expression_and_clamp_its_fires_to_50()
    {
        // given
        await using var app = await JobsDashboardHttpHost.StartAsync(_time, _log, AbortToken);
        using var client = JobsDashboardHttpHost.Client(app);
        var cookie = await JobsDashboardHttpHost.SignInAsync(client, "viewer", AbortToken);

        // when
        using var missing = await JobsDashboardHttpHost.GetAsync(
            client,
            JobsDashboardHttpHost.Api + "/cron-preview",
            cookie,
            AbortToken
        );
        using var clamped = await JobsDashboardHttpHost.GetAsync(
            client,
            JobsDashboardHttpHost.Api
                + "/cron-preview?expression="
                + Uri.EscapeDataString("0 * * * * *")
                + "&count=500",
            cookie,
            AbortToken
        );
        using var invalid = await JobsDashboardHttpHost.GetAsync(
            client,
            JobsDashboardHttpHost.Api + "/cron-preview?expression=" + Uri.EscapeDataString("not a cron expression"),
            cookie,
            AbortToken
        );

        // then
        missing.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        clamped.StatusCode.Should().Be(HttpStatusCode.OK);
        (await JobsDashboardHttpHost.ReadAsync(clamped, AbortToken))
            .GetProperty("nextFires")
            .GetArrayLength()
            .Should()
            .Be(50);
        invalid.StatusCode.Should().Be(HttpStatusCode.OK, "an invalid draft is an answer, shown inline, not an error");
        (await JobsDashboardHttpHost.ReadAsync(invalid, AbortToken))
            .GetProperty("validationMessage")
            .GetString()
            .Should()
            .NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task should_list_the_registered_functions_and_the_live_nodes()
    {
        // given
        await using var app = await JobsDashboardHttpHost.StartAsync(_time, _log, AbortToken);
        using var client = JobsDashboardHttpHost.Client(app);
        var cookie = await JobsDashboardHttpHost.SignInAsync(client, "viewer", AbortToken);

        // when
        using var functions = await JobsDashboardHttpHost.GetAsync(
            client,
            JobsDashboardHttpHost.Api + "/functions",
            cookie,
            AbortToken
        );
        using var nodes = await JobsDashboardHttpHost.GetAsync(
            client,
            JobsDashboardHttpHost.Api + "/nodes",
            cookie,
            AbortToken
        );

        // then
        functions.StatusCode.Should().Be(HttpStatusCode.OK);
        (await JobsDashboardHttpHost.ReadAsync(functions, AbortToken))
            .GetProperty("items")
            .EnumerateArray()
            .Select(item => item.GetProperty("name").GetString())
            .Should()
            .BeEquivalentTo(JobsModuleNode.Charge, JobsModuleNode.Report);
        nodes.StatusCode.Should().Be(HttpStatusCode.OK);
        (await JobsDashboardHttpHost.ReadAsync(nodes, AbortToken))
            .GetProperty("items")
            .ValueKind.Should()
            .Be(JsonValueKind.Array);
    }

    private static IJobPersistenceProvider<TimeJobEntity, CronJobEntity> _Store(WebApplication app) =>
        app.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
}
