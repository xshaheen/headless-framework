// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Coordination;
using Headless.Jobs;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

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
    [InlineData("sort=payload")]
    [InlineData("direction=sideways")]
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
    public async Task should_sort_and_search_the_runs_on_the_server()
    {
        // given
        await using var app = await JobsDashboardHttpHost.StartAsync(_time, _log, AbortToken);
        var now = _time.GetUtcNow();
        var alpha = JobsModuleNode.FailedJob(now, function: "alpha.job");
        var beta = JobsModuleNode.FailedJob(now, function: "beta.job");
        beta.Description = "Refund 50% of the order";
        var gamma = JobsModuleNode.FailedJob(now, function: "gamma.job");
        await _Store(app).AddTimeJobsAsync([beta, gamma, alpha], AbortToken);
        using var client = JobsDashboardHttpHost.Client(app);
        var cookie = await JobsDashboardHttpHost.SignInAsync(client, "viewer", AbortToken);

        // when
        var descending = await _RunIdsAsync(client, cookie, "/runs?sort=function&direction=descending");
        var ascending = await _RunIdsAsync(client, cookie, "/runs?sort=function");
        var searched = await _RunIdsAsync(client, cookie, "/runs?search=" + Uri.EscapeDataString("50% OF"));

        // then
        descending.Should().Equal(gamma.Id, beta.Id, alpha.Id);
        ascending.Should().Equal(alpha.Id, beta.Id, gamma.Id);
        searched.Should().Equal(beta.Id);
    }

    [Fact]
    public async Task should_answer_400_for_a_cursor_sent_with_another_sort_than_it_was_read_with()
    {
        // given
        await using var app = await JobsDashboardHttpHost.StartAsync(_time, _log, AbortToken);
        var now = _time.GetUtcNow();
        await _Store(app)
            .AddTimeJobsAsync(
                [
                    JobsModuleNode.FailedJob(now, function: "alpha.job"),
                    JobsModuleNode.FailedJob(now, function: "beta.job"),
                ],
                AbortToken
            );
        using var client = JobsDashboardHttpHost.Client(app);
        var cookie = await JobsDashboardHttpHost.SignInAsync(client, "viewer", AbortToken);
        using var first = await JobsDashboardHttpHost.GetAsync(
            client,
            JobsDashboardHttpHost.Api + "/runs?sort=function&limit=1",
            cookie,
            AbortToken
        );
        var cursor = Uri.EscapeDataString(
            (await JobsDashboardHttpHost.ReadAsync(first, AbortToken)).GetProperty("nextCursor").GetString()!
        );

        // when
        using var sameSort = await JobsDashboardHttpHost.GetAsync(
            client,
            JobsDashboardHttpHost.Api + "/runs?sort=function&limit=1&after=" + cursor,
            cookie,
            AbortToken
        );
        using var otherSort = await JobsDashboardHttpHost.GetAsync(
            client,
            JobsDashboardHttpHost.Api + "/runs?sort=status&limit=1&after=" + cursor,
            cookie,
            AbortToken
        );

        // then
        sameSort.StatusCode.Should().Be(HttpStatusCode.OK);
        otherSort.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task should_serve_a_runs_retry_settings_and_its_progress_time_only_once_it_reports()
    {
        // given
        await using var app = await JobsDashboardHttpHost.StartAsync(_time, _log, AbortToken);
        var now = _time.GetUtcNow();
        var reporting = JobsModuleNode.IdleJob(now);
        var silent = JobsModuleNode.FailedJob(now);
        silent.Retries = 3;
        silent.RetryIntervals = [10, 30, 60];
        await _Store(app).AddTimeJobsAsync([reporting, silent], AbortToken);
        var claimed = (await _Store(app).AcquireImmediateTimeJobsAsync([reporting.Id], AbortToken))
            .Should()
            .ContainSingle()
            .Subject;
        (
            await _Store(app)
                .UpdateTimeJobProgressAsync(
                    JobsLeaseTables.TimeJob(reporting.Id, claimed.Lease.Generation),
                    new JobProgress(40),
                    AbortToken
                )
        )
            .Should()
            .BeTrue();
        using var client = JobsDashboardHttpHost.Client(app);
        var cookie = await JobsDashboardHttpHost.SignInAsync(client, "viewer", AbortToken);

        // when
        var reported = await _RunAsync(client, cookie, reporting.Id);
        var neverReported = await _RunAsync(client, cookie, silent.Id);

        // then
        reported.TryGetProperty("progressUpdatedAt", out var progressAt).Should().BeTrue();
        progressAt.GetDateTimeOffset().Should().Be(now);
        neverReported.TryGetProperty("progressUpdatedAt", out _).Should().BeFalse();
        neverReported.GetProperty("retries").GetInt32().Should().Be(3);
        neverReported
            .GetProperty("retryIntervals")
            .EnumerateArray()
            .Select(static interval => interval.GetInt32())
            .Should()
            .Equal(10, 30, 60);
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

    private static async Task<List<Guid>> _RunIdsAsync(HttpClient client, string cookie, string path)
    {
        using var response = await JobsDashboardHttpHost.GetAsync(
            client,
            JobsDashboardHttpHost.Api + path,
            cookie,
            AbortToken
        );
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return
        [
            .. (await JobsDashboardHttpHost.ReadAsync(response, AbortToken))
                .GetProperty("items")
                .EnumerateArray()
                .Select(static item => item.GetProperty("id").GetGuid()),
        ];
    }

    private static async Task<JsonElement> _RunAsync(HttpClient client, string cookie, Guid id)
    {
        using var response = await JobsDashboardHttpHost.GetAsync(
            client,
            JobsDashboardHttpHost.Api + "/runs/" + id,
            cookie,
            AbortToken
        );
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return (await JobsDashboardHttpHost.ReadAsync(response, AbortToken)).GetProperty("run").Clone();
    }

    private static IJobPersistenceProvider<TimeJobEntity, CronJobEntity> _Store(WebApplication app) =>
        app.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();

    [Fact]
    public async Task should_serve_one_bucket_per_cairo_day_with_the_23_hour_spring_forward_day_included()
    {
        // given: Cairo moves from +02:00 to +03:00 at 22:00Z on 23 April 2026, so 24 April has no midnight.
        await using var app = await JobsDashboardHttpHost.StartAsync(_time, _log, AbortToken);
        await _Store(app)
            .AddTimeJobsAsync(
                [
                    _Scheduled(JobStatus.Succeeded, new DateTime(2026, 4, 23, 21, 30, 0, DateTimeKind.Utc)),
                    _Scheduled(JobStatus.Failed, new DateTime(2026, 4, 23, 22, 0, 0, DateTimeKind.Utc)),
                    _Scheduled(JobStatus.Succeeded, new DateTime(2026, 4, 24, 20, 59, 0, DateTimeKind.Utc)),
                ],
                AbortToken
            );
        using var client = JobsDashboardHttpHost.Client(app);
        var cookie = await JobsDashboardHttpHost.SignInAsync(client, "viewer", AbortToken);

        // when
        var body = await _SeriesAsync(client, cookie, "from=2026-04-23&to=2026-04-25&zone=Africa/Cairo");

        // then
        var days = body.GetProperty("days").EnumerateArray().ToArray();
        days.Select(day => day.GetProperty("date").GetString())
            .Should()
            .Equal("2026-04-23", "2026-04-24", "2026-04-25");
        days.Select(day => day.GetProperty("start").GetDateTime())
            .Should()
            .Equal(
                new DateTime(2026, 4, 22, 22, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 4, 23, 22, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 4, 24, 21, 0, 0, DateTimeKind.Utc)
            );
        (days[1].GetProperty("end").GetDateTime() - days[1].GetProperty("start").GetDateTime())
            .Should()
            .Be(TimeSpan.FromHours(23));
        _Count(days[0], JobStatus.Succeeded).Should().Be(1);
        _Count(days[1], JobStatus.Failed).Should().Be(1);
        _Count(days[1], JobStatus.Succeeded).Should().Be(1);
        _Count(days[2], JobStatus.Succeeded).Should().Be(0);
        body.GetProperty("zone").GetString().Should().Be("Africa/Cairo");
        body.GetProperty("exact").GetBoolean().Should().BeTrue();
        body.GetProperty("asOf").GetDateTimeOffset().Should().Be(_time.GetUtcNow());
    }

    [Fact]
    public async Task should_serve_the_25_hour_fall_back_day_as_one_cairo_bucket()
    {
        // given: Cairo moves from +03:00 back to +02:00 at 21:00Z on 29 October 2026.
        await using var app = await JobsDashboardHttpHost.StartAsync(_time, _log, AbortToken);
        await _Store(app)
            .AddTimeJobsAsync(
                [_Scheduled(JobStatus.Succeeded, new DateTime(2026, 10, 29, 21, 30, 0, DateTimeKind.Utc))],
                AbortToken
            );
        using var client = JobsDashboardHttpHost.Client(app);
        var cookie = await JobsDashboardHttpHost.SignInAsync(client, "viewer", AbortToken);

        // when
        var body = await _SeriesAsync(client, cookie, "from=2026-10-29&to=2026-10-30&zone=Africa/Cairo");

        // then
        var days = body.GetProperty("days").EnumerateArray().ToArray();
        days.Should().HaveCount(2);
        days[0].GetProperty("start").GetDateTime().Should().Be(new DateTime(2026, 10, 28, 21, 0, 0, DateTimeKind.Utc));
        (days[0].GetProperty("end").GetDateTime() - days[0].GetProperty("start").GetDateTime())
            .Should()
            .Be(TimeSpan.FromHours(25));
        _Count(days[0], JobStatus.Succeeded).Should().Be(1, "23:30 local on the repeated hour is still 29 October");
        _Count(days[1], JobStatus.Succeeded).Should().Be(0);
    }

    [Fact]
    public async Task should_count_scheduled_idle_and_queued_time_jobs_on_future_days()
    {
        // given
        await using var app = await JobsDashboardHttpHost.StartAsync(_time, _log, AbortToken);
        var tomorrow = _time.GetUtcNow().UtcDateTime.Date.AddDays(1);
        await _Store(app)
            .AddTimeJobsAsync(
                [
                    _Scheduled(JobStatus.Idle, tomorrow.AddHours(9)),
                    _Scheduled(JobStatus.Queued, tomorrow.AddHours(10)),
                    _Scheduled(JobStatus.Idle, tomorrow.AddDays(1).AddHours(1)),
                ],
                AbortToken
            );
        using var client = JobsDashboardHttpHost.Client(app);
        var cookie = await JobsDashboardHttpHost.SignInAsync(client, "viewer", AbortToken);

        // when
        var body = await _SeriesAsync(client, cookie, "from=2026-11-05&to=2026-11-07&zone=UTC&kind=TimeJob");

        // then
        var days = body.GetProperty("days").EnumerateArray().ToArray();
        _Count(days[0], JobStatus.Idle).Should().Be(0);
        _Count(days[1], JobStatus.Idle).Should().Be(1);
        _Count(days[1], JobStatus.Queued).Should().Be(1);
        _Count(days[2], JobStatus.Idle).Should().Be(1);
    }

    [Fact]
    public async Task should_serve_a_day_the_store_could_not_read_as_null_and_the_series_as_inexact()
    {
        // given
        await using var app = await JobsDashboardHttpHost.StartAsync(
            _time,
            _log,
            AbortToken,
            configure: services =>
            {
                var factory = services.Last(x => x.ServiceType == typeof(IJobsDashboardBackend)).ImplementationFactory!;
                services.Replace(
                    ServiceDescriptor.Singleton(provider =>
                        InterceptedJobsDashboardBackend.Wrap(
                            (IJobsDashboardBackend)factory(provider),
                            (method, _, proceed) =>
                                string.Equals(
                                    method,
                                    nameof(IJobsDashboardBackend.CountRunsByBucketAsync),
                                    StringComparison.Ordinal
                                )
                                    ? _WithFirstBucketUnread((Task<IReadOnlyList<JobRunBucket>>)proceed()!)
                                    : proceed()
                        )
                    )
                );
            }
        );
        using var client = JobsDashboardHttpHost.Client(app);
        var cookie = await JobsDashboardHttpHost.SignInAsync(client, "viewer", AbortToken);

        // when
        var body = await _SeriesAsync(client, cookie, "from=2026-11-01&to=2026-11-02&zone=UTC");

        // then
        var days = body.GetProperty("days").EnumerateArray().ToArray();
        days[0].GetProperty("counts").ValueKind.Should().Be(JsonValueKind.Null);
        days[1].GetProperty("counts").EnumerateObject().Should().OnlyContain(status => status.Value.GetInt32() == 0);
        days[1].GetProperty("counts").EnumerateObject().Should().HaveCount(Enum.GetValues<JobStatus>().Length);
        body.GetProperty("exact").GetBoolean().Should().BeFalse();
    }

    [Theory]
    [InlineData("from=2026-11-01")]
    [InlineData("from=2026-11-02&to=2026-11-01")]
    [InlineData("from=2026-01-01&to=2026-12-31")]
    [InlineData("from=2026-11-01&to=2026-11-02&zone=Mars/Olympus")]
    [InlineData("from=2026-11-01&to=2026-11-02&kind=Recurring")]
    [InlineData("from=9999-12-01&to=9999-12-31")]
    [InlineData("from=0001-01-01&to=0001-01-02&zone=Pacific/Kiritimati")]
    public async Task should_answer_400_for_a_series_query_it_cannot_read(string query)
    {
        // given
        await using var app = await JobsDashboardHttpHost.StartAsync(_time, _log, AbortToken);
        using var client = JobsDashboardHttpHost.Client(app);
        var cookie = await JobsDashboardHttpHost.SignInAsync(client, "viewer", AbortToken);

        // when
        using var response = await JobsDashboardHttpHost.GetAsync(
            client,
            JobsDashboardHttpHost.Api + "/series/daily?" + query,
            cookie,
            AbortToken
        );

        // then
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task should_mark_the_serving_node_local_with_its_running_count_and_leave_a_peer_without_an_exception()
    {
        // given
        var local = NodeIdentity.Parse("node-a@1");
        var peer = NodeIdentity.Parse("node-b@4");
        var beat = new DateTimeOffset(2026, 11, 5, 11, 59, 30, TimeSpan.Zero);
        var membership = Substitute.For<INodeMembership>();
        membership.Identity.Returns(local);
        membership.ClusterName.Returns("prod");
        membership
            .GetLivenessSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(
                new ValueTask<IReadOnlyList<NodeLivenessSnapshot>>([
                    new NodeLivenessSnapshot(
                        local,
                        NodeLivenessState.Alive,
                        null,
                        new Dictionary<string, string>(),
                        beat
                    ),
                    new NodeLivenessSnapshot(
                        peer,
                        NodeLivenessState.Alive,
                        null,
                        new Dictionary<string, string> { ["headless.jobs.max-concurrency"] = "16" },
                        beat.AddSeconds(-5)
                    ),
                ])
            );
        await using var app = await JobsDashboardHttpHost.StartAsync(
            _time,
            _log,
            AbortToken,
            configure: services => services.AddSingleton(membership)
        );
        await _Store(app)
            .AddTimeJobsAsync(
                [
                    _Running(NodeOwnerTag.Format("prod", peer)),
                    _Running(NodeOwnerTag.Format("prod", peer)),
                    _Running(NodeOwnerTag.Format("prod", local)),
                ],
                AbortToken
            );
        app.Services.GetRequiredService<IJobsNotificationHubSender>().UpdateHostException("Store unreachable");
        using var client = JobsDashboardHttpHost.Client(app);
        var cookie = await JobsDashboardHttpHost.SignInAsync(client, "viewer", AbortToken);

        // when
        var nodes = await _NodesAsync(client, cookie);

        // then
        var serving = nodes.Single(node =>
            string.Equals(node.GetProperty("identity").GetString(), "node-a@1", StringComparison.Ordinal)
        );
        var other = nodes.Single(node =>
            string.Equals(node.GetProperty("identity").GetString(), "node-b@4", StringComparison.Ordinal)
        );
        serving.GetProperty("isLocal").GetBoolean().Should().BeTrue();
        serving.GetProperty("running").GetInt32().Should().Be(1);
        serving.GetProperty("maxConcurrency").GetInt32().Should().Be(Environment.ProcessorCount);
        serving.GetProperty("lastHeartbeatAt").GetDateTimeOffset().Should().Be(beat);
        serving.GetProperty("lastHostException").GetString().Should().Be("Store unreachable");
        serving.GetProperty("lastHostExceptionAt").GetDateTimeOffset().Should().Be(_time.GetUtcNow());
        other.GetProperty("isLocal").GetBoolean().Should().BeFalse();
        other.GetProperty("running").GetInt32().Should().Be(2);
        other.GetProperty("runningCapped").GetBoolean().Should().BeFalse();
        other.GetProperty("maxConcurrency").GetInt32().Should().Be(16);
        other
            .TryGetProperty("lastHostException", out _)
            .Should()
            .BeFalse("a peer's exception is known only on that node");
    }

    [Fact]
    public async Task should_report_no_host_exception_on_a_freshly_started_host()
    {
        // given: the exception lives in the reporting process, so a new host starts without one.
        await using (var before = await JobsDashboardHttpHost.StartAsync(_time, _log, AbortToken))
        {
            before.Services.GetRequiredService<IJobsNotificationHubSender>().UpdateHostException("boom");
        }

        await using var app = await JobsDashboardHttpHost.StartAsync(_time, _log, AbortToken);
        using var client = JobsDashboardHttpHost.Client(app);
        var cookie = await JobsDashboardHttpHost.SignInAsync(client, "viewer", AbortToken);

        // when
        var nodes = await _NodesAsync(client, cookie);

        // then
        var node = nodes.Should().ContainSingle().Subject;
        node.GetProperty("isLocal").GetBoolean().Should().BeTrue();
        node.GetProperty("running").GetInt32().Should().Be(0);
        node.TryGetProperty("lastHostException", out _).Should().BeFalse();
        node.TryGetProperty("lastHeartbeatAt", out _).Should().BeFalse("a host without membership has no heartbeat");
    }

    private static async Task<JsonElement> _SeriesAsync(HttpClient client, string cookie, string query)
    {
        using var response = await JobsDashboardHttpHost.GetAsync(
            client,
            JobsDashboardHttpHost.Api + "/series/daily?" + query,
            cookie,
            AbortToken
        );
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return await JobsDashboardHttpHost.ReadAsync(response, AbortToken);
    }

    private static async Task<JsonElement[]> _NodesAsync(HttpClient client, string cookie)
    {
        using var response = await JobsDashboardHttpHost.GetAsync(
            client,
            JobsDashboardHttpHost.Api + "/nodes",
            cookie,
            AbortToken
        );
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return [.. (await JobsDashboardHttpHost.ReadAsync(response, AbortToken)).GetProperty("items").EnumerateArray()];
    }

    private static int _Count(JsonElement day, JobStatus status) =>
        day.GetProperty("counts").GetProperty(status.ToString()).GetInt32();

    private static async Task<IReadOnlyList<JobRunBucket>> _WithFirstBucketUnread(
        Task<IReadOnlyList<JobRunBucket>> read
    )
    {
        var buckets = await read;

        return [buckets[0] with { Counts = null }, .. buckets.Skip(1)];
    }

    private TimeJobEntity _Scheduled(JobStatus status, DateTime executionTime)
    {
        var finished = status is JobStatus.Succeeded or JobStatus.Failed;

        return new TimeJobEntity
        {
            Id = Guid.NewGuid(),
            Function = JobsModuleNode.Charge,
            Status = status,
            ExecutionTime = executionTime,
            ExecutedAt = finished ? new DateTimeOffset(executionTime) : null,
            ExceptionMessage = status == JobStatus.Failed ? "boom" : null,
            CreatedAt = _time.GetUtcNow().AddDays(-300),
            UpdatedAt = _time.GetUtcNow().AddDays(-300),
        };
    }

    private TimeJobEntity _Running(string owner)
    {
        var job = _Scheduled(JobStatus.InProgress, _time.GetUtcNow().UtcDateTime.AddMinutes(-1));
        job.Lease = TestLeases.Held(owner, _time.GetUtcNow().AddMinutes(5));

        return job;
    }
}
