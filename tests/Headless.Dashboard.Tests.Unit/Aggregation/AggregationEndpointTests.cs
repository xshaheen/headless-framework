// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Headless.Dashboard;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Net.Http.Headers;

namespace Tests.Aggregation;

/// <summary>
/// Needs you, Overview, id lookup, and related objects on a real host: every module answers in parallel, and one slow,
/// failing, refused, or hidden module changes only its own part of the answer.
/// </summary>
public sealed class AggregationEndpointTests : TestBase
{
    private const string _TraceId = "4bf92f3577b34da6a3ce929d0e0e4736";
    private const string _DiagBViewers = "diag-b.viewers";

    private static readonly DashboardCommand _Requeue = new()
    {
        Id = "diag-a.requeue",
        Label = "Requeue",
        ObjectKind = "run",
        Confirmation = DashboardConfirmation.Confirm,
        RequiresReason = true,
    };

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 5, 9, 0, 0, TimeSpan.Zero));
    private readonly DiagAModule _diagA = new();
    private readonly DiagBModule _diagB = new();

    [Fact]
    public async Task should_answer_with_the_fast_module_and_mark_the_slow_one_stale_when_the_timeout_passes()
    {
        var slowCalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _diagA.Attention = _ => _Items(_Item("a-1", count: 3));
        _diagB.Attention = async cancellationToken =>
        {
            slowCalled.SetResult();
            await new TaskCompletionSource().Task.WaitAsync(cancellationToken);
            return [];
        };

        await using var app = await _StartAsync();
        using var client = app.GetTestClient();

        var pending = client.GetAsync("/dash/api/needs-you", AbortToken);
        await slowCalled.Task.WaitAsync(AbortToken);
        _time.Advance(TimeSpan.FromSeconds(2));
        using var response = await pending;

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var modules = (await _ReadAsync(response)).GetProperty("modules");
        modules.GetArrayLength().Should().Be(2);
        modules[0].GetProperty("id").GetString().Should().Be("diag-a");
        modules[0].GetProperty("stale").GetBoolean().Should().BeFalse();
        modules[0].GetProperty("items")[0].GetProperty("id").GetString().Should().Be("a-1");
        modules[1].GetProperty("id").GetString().Should().Be("diag-b");
        modules[1].GetProperty("stale").GetBoolean().Should().BeTrue();
        modules[1].GetProperty("items").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task should_honor_a_configured_aggregation_timeout()
    {
        var slowCalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _diagB.Overview = async cancellationToken =>
        {
            slowCalled.SetResult();
            await new TaskCompletionSource().Task.WaitAsync(cancellationToken);
            return [];
        };

        await using var app = await _StartAsync(services: services =>
            services.Configure<HeadlessDashboardOptions>(options =>
                options.AggregationTimeout = TimeSpan.FromMilliseconds(500)
            )
        );
        using var client = app.GetTestClient();

        var pending = client.GetAsync("/dash/api/overview", AbortToken);
        await slowCalled.Task.WaitAsync(AbortToken);
        _time.Advance(TimeSpan.FromMilliseconds(500));
        using var response = await pending;

        var modules = (await _ReadAsync(response)).GetProperty("modules");
        modules[1].GetProperty("stale").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task should_mark_a_throwing_module_stale_and_still_answer_with_the_others()
    {
        _diagA.Attention = _ => throw new InvalidOperationException("store is down");
        _diagB.Attention = _ => _Items(_Item("b-1", count: 1));

        await using var app = await _StartAsync();
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/dash/api/needs-you", AbortToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var modules = (await _ReadAsync(response)).GetProperty("modules");
        modules[0].GetProperty("stale").GetBoolean().Should().BeTrue();
        modules[1].GetProperty("stale").GetBoolean().Should().BeFalse();
        modules[1].GetProperty("items")[0].GetProperty("id").GetString().Should().Be("b-1");
    }

    [Fact]
    public async Task should_leave_a_refused_module_out_of_every_aggregation()
    {
        var refusedCalled = false;
        _diagB.SdkVersion = "2.0.0";
        _diagB.Attention = _ =>
        {
            refusedCalled = true;
            return _Items(_Item("b-1", count: 1));
        };
        _diagA.Attention = _ => _Items(_Item("a-1", count: 1));

        await using var app = await _StartAsync();
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/dash/api/needs-you", AbortToken);

        var modules = (await _ReadAsync(response)).GetProperty("modules");
        modules.EnumerateArray().Select(m => m.GetProperty("id").GetString()).Should().Equal("diag-a");
        refusedCalled.Should().BeFalse();
    }

    [Fact]
    public async Task should_give_a_user_none_of_the_items_of_a_module_they_may_not_view()
    {
        _diagA.Attention = _ => _Items(_Item("a-1", count: 1));
        _diagB.Attention = _ => _Items(_Item("b-1", count: 1));

        await using var app = await _StartAsync(
            services: services => services.Configure<DiagBModuleOptions>(o => o.ViewPolicy = _DiagBViewers),
            hostAuthentication: true
        );
        using var client = _HttpsClient(app);

        using var outsider = await _GetSignedInAsync(client, "/dash/api/needs-you", roles: "");
        using var viewer = await _GetSignedInAsync(client, "/dash/api/needs-you", roles: "diag-b-viewer");

        (await _ReadAsync(outsider))
            .GetProperty("modules")
            .EnumerateArray()
            .Select(m => m.GetProperty("id").GetString())
            .Should()
            .Equal("diag-a");
        (await _ReadAsync(viewer))
            .GetProperty("modules")
            .EnumerateArray()
            .Select(m => m.GetProperty("id").GetString())
            .Should()
            .Equal("diag-a", "diag-b");
    }

    [Fact]
    public async Task should_return_the_reference_of_the_run_whose_pasted_id_the_resolver_finds()
    {
        _diagA.Resolve = query =>
            string.Equals(query, "run-42", StringComparison.Ordinal)
                ? Task.FromResult<IReadOnlyList<DashboardObject>>([_Run("diag-a", "run-42")])
                : Task.FromResult<IReadOnlyList<DashboardObject>>([]);

        await using var app = await _StartAsync();
        using var client = app.GetTestClient();

        using var response = await _PostAsync(client, "/dash/api/resolve", new { query = " run-42 " });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var modules = (await _ReadAsync(response)).GetProperty("modules");
        var target = modules[0].GetProperty("items")[0].GetProperty("target");
        target.GetProperty("module").GetString().Should().Be("diag-a");
        target.GetProperty("kind").GetString().Should().Be("run");
        target.GetProperty("id").GetString().Should().Be("run-42");
        modules[1].GetProperty("items").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task should_refuse_an_empty_pasted_id()
    {
        await using var app = await _StartAsync();
        using var client = app.GetTestClient();

        using var response = await _PostAsync(client, "/dash/api/resolve", new { query = " " });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task should_include_the_second_instance_related_group_under_its_label()
    {
        _diagB.GroupIds = ["diag-b.published"];
        _diagB.Related = target =>
            Task.FromResult<IReadOnlyList<DashboardRelatedGroup>>([
                new DashboardRelatedGroup
                {
                    Id = "diag-b.published",
                    Label = "Published runs",
                    Items = [_Run("diag-b", $"from-{target.Id}")],
                },
            ]);

        await using var app = await _StartAsync();
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/dash/api/related?module=diag-a&kind=run&id=run-42", AbortToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var group = (await _ReadAsync(response))
            .GetProperty("groups")
            .EnumerateArray()
            .Should()
            .ContainSingle()
            .Subject;
        group.GetProperty("label").GetString().Should().Be("Published runs");
        group.GetProperty("items")[0].GetProperty("target").GetProperty("id").GetString().Should().Be("from-run-42");
    }

    [Fact]
    public async Task should_answer_related_objects_without_the_group_when_the_second_instance_is_removed()
    {
        await using var app = await _StartAsync(withDiagB: false);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/dash/api/related?module=diag-a&kind=run&id=run-42", AbortToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await _ReadAsync(response);
        body.GetProperty("groups").GetArrayLength().Should().Be(0);
        body.GetProperty("stale").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task should_drop_a_related_group_the_host_disabled()
    {
        _diagB.GroupIds = ["diag-b.published"];
        _diagB.Related = _ =>
            Task.FromResult<IReadOnlyList<DashboardRelatedGroup>>([
                new DashboardRelatedGroup
                {
                    Id = "diag-b.published",
                    Label = "Published runs",
                    Items = [_Run("diag-b", "b-1")],
                },
            ]);

        await using var app = await _StartAsync(configure: dashboard =>
            dashboard.Contributions(c => c.Disable("diag-b.published"))
        );
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/dash/api/related?module=diag-a&kind=run&id=run-42", AbortToken);

        (await _ReadAsync(response)).GetProperty("groups").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task should_refuse_related_objects_for_a_module_the_user_may_not_view()
    {
        await using var app = await _StartAsync(
            services: services => services.Configure<DiagBModuleOptions>(o => o.ViewPolicy = _DiagBViewers),
            hostAuthentication: true
        );
        using var client = _HttpsClient(app);

        using var response = await _GetSignedInAsync(
            client,
            "/dash/api/related?module=diag-b&kind=run&id=run-42",
            roles: ""
        );

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task should_answer_not_found_for_related_objects_of_an_unknown_module()
    {
        await using var app = await _StartAsync();
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/dash/api/related?module=nope&kind=run&id=run-42", AbortToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task should_offer_open_trace_with_the_template_filled_when_the_object_carries_a_trace_id()
    {
        _diagA.Resolve = _ =>
            Task.FromResult<IReadOnlyList<DashboardObject>>([_Run("diag-a", "run-42") with { TraceId = _TraceId }]);

        await using var app = await _StartAsync(configure: dashboard =>
            dashboard.SetTraceLinkTemplate("https://apm.example/trace/{traceId}?tab=spans")
        );
        using var client = app.GetTestClient();

        using var response = await _PostAsync(client, "/dash/api/resolve", new { query = "run-42" });

        var item = (await _ReadAsync(response)).GetProperty("modules")[0].GetProperty("items")[0];
        item.GetProperty("traceId").GetString().Should().Be(_TraceId);
        item.GetProperty("traceUrl").GetString().Should().Be($"https://apm.example/trace/{_TraceId}?tab=spans");
    }

    [Fact]
    public async Task should_offer_no_trace_link_when_the_host_set_no_template()
    {
        _diagA.Resolve = _ =>
            Task.FromResult<IReadOnlyList<DashboardObject>>([_Run("diag-a", "run-42") with { TraceId = _TraceId }]);

        await using var app = await _StartAsync();
        using var client = app.GetTestClient();

        using var response = await _PostAsync(client, "/dash/api/resolve", new { query = "run-42" });

        var item = (await _ReadAsync(response)).GetProperty("modules")[0].GetProperty("items")[0];
        item.GetProperty("traceUrl").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task should_return_a_count_above_the_cap_as_the_cap_with_capped_set()
    {
        _diagA.Attention = _ => _Items(_Item("a-1", count: 12_000), _Item("a-2", count: 9_999));

        await using var app = await _StartAsync(withDiagB: false);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/dash/api/needs-you", AbortToken);

        var items = (await _ReadAsync(response)).GetProperty("modules")[0].GetProperty("items");
        items[0].GetProperty("count").GetInt64().Should().Be(10_000);
        items[0].GetProperty("capped").GetBoolean().Should().BeTrue();
        items[1].GetProperty("count").GetInt64().Should().Be(9_999);
        items[1].GetProperty("capped").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task should_return_the_recommended_command_with_its_group_filter_and_growth_when_needs_you_asks()
    {
        var lastSeenAt = _time.GetUtcNow().AddMinutes(-5);
        _diagA.Commands = [_Requeue];
        _diagA.Attention = _ =>
            _Items(
                _Item("a-1", count: 4) with
                {
                    RecommendedCommandId = _Requeue.Id,
                    GroupFilter = JsonSerializer.SerializeToElement(new { error = "boom" }),
                    LastSeenAt = lastSeenAt,
                    RecentCount = 2,
                },
                _Item("a-2", count: 1) with
                {
                    RecommendedCommandId = "diag-a.unknown",
                    GroupFilter = JsonSerializer.SerializeToElement(new { error = "other" }),
                }
            );

        await using var app = await _StartAsync(withDiagB: false);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/dash/api/needs-you", AbortToken);

        var items = (await _ReadAsync(response)).GetProperty("modules")[0].GetProperty("items");
        var recommended = items[0].GetProperty("recommended");
        recommended.GetProperty("command").GetProperty("id").GetString().Should().Be(_Requeue.Id);
        recommended.GetProperty("command").GetProperty("label").GetString().Should().Be("Requeue");
        recommended.GetProperty("command").GetProperty("confirmation").GetString().Should().Be("confirm");
        recommended.GetProperty("command").GetProperty("canRun").GetBoolean().Should().BeTrue();
        recommended.GetProperty("filter").GetProperty("error").GetString().Should().Be("boom");
        items[0].GetProperty("lastSeenAt").GetDateTimeOffset().Should().Be(lastSeenAt);
        items[0].GetProperty("recentCount").GetInt64().Should().Be(2);
        // A command the module does not offer the UI, such as one the host disabled, is not recommended.
        items[1].GetProperty("recommended").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task should_recommend_the_command_disabled_with_needs_operate_permission_when_a_viewer_asks()
    {
        _diagA.Commands = [_Requeue];
        _diagA.Attention = _ =>
            _Items(
                _Item("a-1", count: 4) with
                {
                    RecommendedCommandId = _Requeue.Id,
                    GroupFilter = JsonSerializer.SerializeToElement(new { error = "boom" }),
                }
            );

        await using var app = await _StartAsync(
            withDiagB: false,
            services: services => services.Configure<DiagAModuleOptions>(o => o.OperatePolicy = _DiagBViewers),
            hostAuthentication: true
        );
        using var client = _HttpsClient(app);

        using var response = await _GetSignedInAsync(client, "/dash/api/needs-you", roles: "");

        var command = (await _ReadAsync(response))
            .GetProperty("modules")[0]
            .GetProperty("items")[0]
            .GetProperty("recommended")
            .GetProperty("command");
        command.GetProperty("canRun").GetBoolean().Should().BeFalse();
        command.GetProperty("disabledReason").GetString().Should().Be("Needs operate permission");
    }

    [Fact]
    public async Task should_draw_at_most_three_overview_figures_per_module()
    {
        _diagA.Overview = _ =>
            Task.FromResult<IReadOnlyList<DashboardFigure>>([
                .. Enumerable
                    .Range(1, 5)
                    .Select(i => new DashboardFigure
                    {
                        Id = $"f{i}",
                        Label = $"Figure {i}",
                        Value = i,
                    }),
            ]);

        await using var app = await _StartAsync(withDiagB: false);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/dash/api/overview", AbortToken);

        var figures = (await _ReadAsync(response)).GetProperty("modules")[0].GetProperty("items");
        figures.EnumerateArray().Select(f => f.GetProperty("id").GetString()).Should().Equal("f1", "f2", "f3");
    }

    [Fact]
    public async Task should_send_each_modules_schedule_tracks_beside_its_overview_figures()
    {
        var at = new DateTimeOffset(2026, 11, 5, 12, 30, 0, TimeSpan.Zero);
        _diagA.Schedule = _ =>
            Task.FromResult<IReadOnlyList<DashboardScheduleTrack>>([
                new DashboardScheduleTrack
                {
                    Id = "billing.charge",
                    Label = "billing.charge",
                    Ticks =
                    [
                        new DashboardScheduleTick
                        {
                            Id = "r1",
                            At = at,
                            Label = "failed",
                            Tone = "fail",
                            Target = new ObjectRef
                            {
                                Module = "diag-a",
                                Kind = "run",
                                Id = "r1",
                            },
                        },
                    ],
                },
            ]);

        await using var app = await _StartAsync(withDiagB: false);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/dash/api/overview", AbortToken);

        var track = (await _ReadAsync(response)).GetProperty("modules")[0].GetProperty("tracks")[0];
        track.GetProperty("label").GetString().Should().Be("billing.charge");
        var tick = track.GetProperty("ticks")[0];
        tick.GetProperty("at").GetDateTimeOffset().Should().Be(at);
        tick.GetProperty("tone").GetString().Should().Be("fail");
        tick.GetProperty("target").GetProperty("id").GetString().Should().Be("r1");
    }

    [Fact]
    public async Task should_send_the_payload_masked_by_the_module_formatter_when_an_operator_opens_it()
    {
        _diagA.ObjectKinds = ["run"];
        _diagA.Payload = (_, _) =>
            Task.FromResult<DashboardPayload?>(
                new DashboardPayload
                {
                    Content = """{"user":"ada","password":"hunter2"}""",
                    ContentType = "application/json",
                }
            );
        _diagA.Format = payload =>
            payload with
            {
                Content = payload.Content.Replace("hunter2", "***", StringComparison.Ordinal),
            };
        await using var app = await _StartAsync();
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/dash/api/payload?module=diag-a&kind=run&id=run-42", AbortToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await _ReadAsync(response);
        body.GetProperty("content").GetString().Should().Be("""{"user":"ada","password":"***"}""");
        body.GetProperty("contentType").GetString().Should().Be("application/json");
    }

    [Fact]
    public async Task should_answer_not_found_when_the_module_has_no_payload_for_the_object()
    {
        await using var app = await _StartAsync();
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/dash/api/payload?module=diag-a&kind=run&id=gone", AbortToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task should_refuse_the_payload_of_a_module_the_user_may_not_view()
    {
        _diagB.Payload = (_, _) =>
            Task.FromResult<DashboardPayload?>(new DashboardPayload { Content = "secret", ContentType = "text/plain" });
        await using var app = await _StartAsync(
            services: services => services.Configure<DiagBModuleOptions>(o => o.ViewPolicy = _DiagBViewers),
            hostAuthentication: true
        );
        using var client = _HttpsClient(app);

        using var response = await _GetSignedInAsync(
            client,
            "/dash/api/payload?module=diag-b&kind=run&id=run-42",
            roles: ""
        );

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task should_answer_gateway_timeout_when_the_payload_read_outlasts_the_timeout()
    {
        var called = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _diagA.Payload = async (_, token) =>
        {
            called.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return null;
        };
        await using var app = await _StartAsync();
        using var client = app.GetTestClient();

        var pending = client.GetAsync("/dash/api/payload?module=diag-a&kind=run&id=run-42", AbortToken);
        await called.Task.WaitAsync(AbortToken);
        _time.Advance(TimeSpan.FromSeconds(2));
        using var response = await pending;

        response.StatusCode.Should().Be(HttpStatusCode.GatewayTimeout);
    }

    [Theory]
    [InlineData("GET", "/dash/api/needs-you")]
    [InlineData("GET", "/dash/api/overview")]
    [InlineData("POST", "/dash/api/resolve")]
    [InlineData("POST", "/dash/api/trace")]
    [InlineData("GET", "/dash/api/related?module=diag-a&kind=run&id=run-42")]
    [InlineData("GET", "/dash/api/payload?module=diag-a&kind=run&id=run-42")]
    public async Task should_answer_401_and_call_no_module_when_a_request_has_no_session_under_basic_authentication(
        string method,
        string path
    )
    {
        var called = false;
        _diagA.Attention = _ =>
        {
            called = true;
            return _Items(_Item("a-1", count: 1));
        };
        _diagA.Payload = (_, _) =>
        {
            called = true;
            return Task.FromResult<DashboardPayload?>(
                new DashboardPayload { Content = "secret", ContentType = "text/plain" }
            );
        };
        _diagA.Resolve = _ =>
        {
            called = true;
            return Task.FromResult<IReadOnlyList<DashboardObject>>([]);
        };
        _diagA.Trace = (_, _) =>
        {
            called = true;
            return Task.FromResult(new DashboardTraceAnswer { Items = [] });
        };
        await using var app = await _StartAsync(auth: dashboard => dashboard.WithBasicAuth("operator", "s3cret-pass"));
        using var client = _HttpsClient(app);

        using var request = new HttpRequestMessage(new HttpMethod(method), path);

        if (string.Equals(method, "POST", StringComparison.Ordinal))
        {
            request.Content = JsonContent.Create(new { query = "run-42", correlationId = "ord-7" });
            request.Headers.Add("X-Requested-With", "XMLHttpRequest");
        }

        using var response = await client.SendAsync(request, AbortToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        called.Should().BeFalse();
    }

    [Fact]
    public async Task should_answer_401_when_a_request_has_no_session_under_host_authentication()
    {
        await using var app = await _StartAsync(hostAuthentication: true);
        using var client = _HttpsClient(app);

        using var response = await client.GetAsync("/dash/api/needs-you", AbortToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task should_answer_every_count_of_the_product_from_each_module_with_capped_set_at_the_cap()
    {
        _diagA.Counts = _ =>
            _Counts(
                ("Failed", new DashboardCount { Count = 12_000 }),
                ("Queued", new DashboardCount { Count = 10_000, Capped = true }),
                ("Idle", new DashboardCount { Count = 9_999 })
            );
        // A key the first module already answered keeps the first module's count.
        _diagB.Counts = _ =>
            _Counts(("Failed", new DashboardCount { Count = 5 }), ("Cancelled", new DashboardCount { Count = 2 }));

        await using var app = await _StartAsync();
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/dash/api/counts?product=platform", AbortToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await _ReadAsync(response);
        body.GetProperty("product").GetString().Should().Be("platform");
        var counts = body.GetProperty("counts");
        counts.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("Failed", "Queued", "Idle", "Cancelled");
        _Count(counts, "Failed").Should().Be((10_000, true));
        _Count(counts, "Queued").Should().Be((10_000, true));
        _Count(counts, "Idle").Should().Be((9_999, false));
        _Count(counts, "Cancelled").Should().Be((2, false));
        body.GetProperty("stale").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task should_ask_only_the_modules_of_the_requested_product_and_mark_a_failing_one_stale()
    {
        var otherCalled = false;
        _diagA.Counts = _ => throw new InvalidOperationException("store down");
        _diagB.Product = "jobs";
        _diagB.Counts = _ =>
        {
            otherCalled = true;
            return _Counts(("Failed", new DashboardCount { Count = 1 }));
        };

        await using var app = await _StartAsync();
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/dash/api/counts?product=platform", AbortToken);

        var body = await _ReadAsync(response);
        body.GetProperty("counts").EnumerateObject().Should().BeEmpty();
        body.GetProperty("stale")
            .EnumerateArray()
            .Select(m => m.GetProperty("id").GetString())
            .Should()
            .Equal("diag-a");
        otherCalled.Should().BeFalse();
    }

    [Fact]
    public async Task should_pass_a_partial_count_through_with_its_unavailable_sources_and_keep_stale_for_a_failed_module()
    {
        _diagA.Counts = _ =>
            _Counts(
                ("Failed", new DashboardCount { Count = 48_120, UnavailableSources = ["outbox-2"] }),
                ("Queued", new DashboardCount { Count = 3 })
            );
        _diagB.Counts = _ => throw new InvalidOperationException("store down");

        await using var app = await _StartAsync();
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/dash/api/counts?product=platform", AbortToken);

        var body = await _ReadAsync(response);
        var counts = body.GetProperty("counts");
        // A partial count above the cap still stops at it, and keeps the sources it leaves out.
        _Count(counts, "Failed").Should().Be((10_000, true));
        _Strings(counts.GetProperty("Failed").GetProperty("unavailable")).Should().Equal("outbox-2");
        counts.GetProperty("Queued").TryGetProperty("unavailable", out _).Should().BeFalse();
        body.GetProperty("stale")
            .EnumerateArray()
            .Select(m => m.GetProperty("id").GetString())
            .Should()
            .Equal("diag-b");
    }

    [Fact]
    public async Task should_list_a_partial_modules_attention_and_overview_with_its_unavailable_sources_and_not_as_stale()
    {
        _diagB.UnavailableSources = ["node-c"];
        _diagB.Attention = _ =>
            Task.FromResult<IReadOnlyList<DashboardAttentionItem>>([
                new DashboardAttentionItem
                {
                    Id = "i-1",
                    GroupKey = "g",
                    Title = "Failed on node-b",
                    Count = 2,
                },
            ]);
        _diagB.Overview = _ =>
            Task.FromResult<IReadOnlyList<DashboardFigure>>([
                new DashboardFigure
                {
                    Id = "f1",
                    Label = "Received",
                    Value = 7,
                },
            ]);

        await using var app = await _StartAsync();
        using var client = app.GetTestClient();

        using var needsYou = await client.GetAsync("/dash/api/needs-you", AbortToken);
        using var overview = await client.GetAsync("/dash/api/overview", AbortToken);

        var attention = _ModuleSection(await _ReadAsync(needsYou), "diag-b");
        attention.GetProperty("stale").GetBoolean().Should().BeFalse();
        attention.GetProperty("items")[0].GetProperty("id").GetString().Should().Be("i-1");
        _Strings(attention.GetProperty("unavailable")).Should().Equal("node-c");

        var figures = _ModuleSection(await _ReadAsync(overview), "diag-b");
        figures.GetProperty("stale").GetBoolean().Should().BeFalse();
        figures.GetProperty("items")[0].GetProperty("id").GetString().Should().Be("f1");
        _Strings(figures.GetProperty("unavailable")).Should().Equal("node-c");

        // A module that answers through the default members, as the Jobs module does, answers as it always has.
        var whole = _ModuleSection(await _ReadAsync(overview), "diag-a");
        whole.TryGetProperty("unavailable", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("billing")]
    [InlineData("jobs")]
    public async Task should_answer_not_found_for_counts_of_a_product_no_module_joins(string product)
    {
        await using var app = await _StartAsync();
        using var client = app.GetTestClient();

        using var response = await client.GetAsync($"/dash/api/counts?product={product}", AbortToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task should_refuse_counts_without_a_product()
    {
        await using var app = await _StartAsync();
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/dash/api/counts", AbortToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task should_refuse_counts_of_a_product_whose_modules_the_user_may_not_view()
    {
        _diagB.Product = "jobs";
        _diagB.Counts = _ => _Counts(("Failed", new DashboardCount { Count = 4 }));

        await using var app = await _StartAsync(
            services: services => services.Configure<DiagBModuleOptions>(o => o.ViewPolicy = _DiagBViewers),
            hostAuthentication: true
        );
        using var client = _HttpsClient(app);

        using var outsider = await _GetSignedInAsync(client, "/dash/api/counts?product=jobs", roles: "");
        using var viewer = await _GetSignedInAsync(client, "/dash/api/counts?product=jobs", roles: "diag-b-viewer");

        outsider.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        viewer.StatusCode.Should().Be(HttpStatusCode.OK);
        _Count((await _ReadAsync(viewer)).GetProperty("counts"), "Failed").Should().Be((4, false));
    }

    [Fact]
    public async Task should_answer_a_trace_with_both_modules_items_in_one_tree_and_each_modules_section()
    {
        string? askedFor = null;
        _diagA.Trace = (request, _) =>
        {
            askedFor = request.CorrelationId;
            return _TraceAnswer(_TraceRun("run-1", at: 0));
        };
        _diagB.Trace = (_, _) =>
            _TraceAnswer(
                new DashboardTraceAnswer
                {
                    Items = [_TracePublished("msg-1", at: 1, causedByRun: "run-1")],
                    Limited = true,
                    ListLink = new DashboardTraceListLink
                    {
                        Path = "published",
                        State = JsonSerializer.SerializeToElement(new { correlationId = "ord-7 " }),
                    },
                }
            );

        await using var app = await _StartAsync();
        using var client = app.GetTestClient();

        using var response = await _PostAsync(client, "/dash/api/trace", new { correlationId = "ord-7 " });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        askedFor.Should().Be("ord-7 ", "a correlation id is matched exactly, so it is never trimmed");
        var body = await _ReadAsync(response);
        body.GetProperty("hidden").GetInt32().Should().Be(0);
        _TraceShape(body).Should().Equal(("diag-a", "run-1", 0), ("diag-b", "msg-1", 1));
        var child = body.GetProperty("rows")[1];
        child.GetProperty("timestampKind").GetString().Should().Be("added");
        child.GetProperty("tags").GetArrayLength().Should().Be(0);
        body.GetProperty("rows")[0].GetProperty("timestampKind").GetString().Should().Be("ran");
        var diagA = _ModuleSection(body, "diag-a");
        diagA.GetProperty("limited").GetBoolean().Should().BeFalse();
        diagA.GetProperty("count").GetInt32().Should().Be(1);
        diagA.TryGetProperty("list", out _).Should().BeFalse();
        var diagB = _ModuleSection(body, "diag-b");
        diagB.GetProperty("stale").GetBoolean().Should().BeFalse();
        diagB.GetProperty("limited").GetBoolean().Should().BeTrue();
        diagB.GetProperty("list").GetProperty("path").GetString().Should().Be("published");
        diagB.GetProperty("list").GetProperty("state").GetProperty("correlationId").GetString().Should().Be("ord-7 ");
    }

    [Fact]
    public async Task should_answer_a_trace_with_the_fast_modules_items_and_the_slow_module_stale_when_the_timeout_passes()
    {
        var slowCalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _diagA.Trace = (_, _) => _TraceAnswer(_TraceRun("run-1", at: 0));
        _diagB.Trace = async (_, cancellationToken) =>
        {
            slowCalled.SetResult();
            await new TaskCompletionSource().Task.WaitAsync(cancellationToken);
            return new DashboardTraceAnswer { Items = [] };
        };

        await using var app = await _StartAsync();
        using var client = app.GetTestClient();

#pragma warning disable CA2025 // False positive: the test awaits the response below, before the client is disposed.
        var pending = _PostAsync(client, "/dash/api/trace", new { correlationId = "ord-7" });
#pragma warning restore CA2025
        await slowCalled.Task.WaitAsync(AbortToken);
        _time.Advance(TimeSpan.FromSeconds(2));
        using var response = await pending;

        var body = await _ReadAsync(response);
        _ModuleSection(body, "diag-b").GetProperty("stale").GetBoolean().Should().BeTrue();
        _ModuleSection(body, "diag-a").GetProperty("stale").GetBoolean().Should().BeFalse();
        _TraceShape(body).Should().Equal(("diag-a", "run-1", 0));
    }

    [Fact]
    public async Task should_count_a_trace_module_the_user_may_not_view_as_hidden_without_calling_or_naming_it()
    {
        var hiddenCalled = false;
        // The run was caused by a message only the hidden module holds.
        _diagA.Trace = (_, _) => _TraceAnswer(_TraceRun("run-1", at: 0) with { Causation = _MessageKey("msg-1") });
        _diagB.Trace = (_, _) =>
        {
            hiddenCalled = true;
            return _TraceAnswer(_TracePublished("msg-1", at: 0, causedByRun: null));
        };

        await using var app = await _StartAsync(
            services: services => services.Configure<DiagBModuleOptions>(o => o.ViewPolicy = _DiagBViewers),
            hostAuthentication: true
        );
        using var client = _HttpsClient(app);

        using var response = await _GetSignedInAsync(
            client,
            "/dash/api/trace",
            roles: "",
            postBody: new { correlationId = "ord-7" }
        );

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        hiddenCalled.Should().BeFalse();
        var json = await response.Content.ReadAsStringAsync(AbortToken);
        json.Should().NotContain("diag-b");
        using var document = JsonDocument.Parse(json);
        var body = document.RootElement;
        body.GetProperty("hidden").GetInt32().Should().Be(1);
        body.GetProperty("modules")
            .EnumerateArray()
            .Select(m => m.GetProperty("id").GetString())
            .Should()
            .Equal("diag-a");
        _Strings(body.GetProperty("rows")[0].GetProperty("tags")).Should().Equal("parent-not-on-page");
    }

    [Fact]
    public async Task should_answer_a_trace_whose_items_cause_each_other_with_the_cycle_broken_and_tagged()
    {
        _diagA.Trace = (_, _) =>
            _TraceAnswer(
                _TraceRun("run-a", at: 0) with
                {
                    Causation = _RunKey("run-b"),
                },
                _TraceRun("run-b", at: 1) with
                {
                    Causation = _RunKey("run-a"),
                }
            );

        await using var app = await _StartAsync();
        using var client = app.GetTestClient();

        using var response = await _PostAsync(client, "/dash/api/trace", new { correlationId = "ord-7" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await _ReadAsync(response);
        _TraceShape(body).Should().Equal(("diag-a", "run-a", 0), ("diag-a", "run-b", 1));
        _Strings(body.GetProperty("rows")[0].GetProperty("tags")).Should().Equal("cycle");
    }

    [Theory]
    [InlineData("""{"correlationId":""}""")]
    [InlineData("{}")]
    [InlineData("{\"correlationId\":\"" + _Id201 + "\"}")]
    [InlineData("""{"correlationId":""")]
    public async Task should_refuse_a_trace_whose_correlation_id_is_empty_too_long_or_unreadable(string json)
    {
        var called = false;
        _diagA.Trace = (_, _) =>
        {
            called = true;
            return _TraceAnswer();
        };

        await using var app = await _StartAsync();
        using var client = app.GetTestClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/dash/api/trace")
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Requested-With", "XMLHttpRequest");
        using var response = await client.SendAsync(request, AbortToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        called.Should().BeFalse();
    }

    [Fact]
    public async Task should_answer_a_trace_for_a_correlation_id_of_exactly_200_characters()
    {
        await using var app = await _StartAsync();
        using var client = app.GetTestClient();

        using var response = await _PostAsync(client, "/dash/api/trace", new { correlationId = _Id201[..200] });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task should_answer_405_to_a_get_of_the_old_resolve_query_route()
    {
        var called = false;
        _diagA.Resolve = _ =>
        {
            called = true;
            return Task.FromResult<IReadOnlyList<DashboardObject>>([]);
        };

        await using var app = await _StartAsync();
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/dash/api/resolve?q=run-42", AbortToken);

        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        response.Content.Headers.Allow.Should().Equal("POST");
        called.Should().BeFalse();
    }

    [Fact]
    public async Task should_never_write_the_correlation_id_to_a_log_at_information_level_or_above()
    {
        const string correlationId = "ord-secret-7731";
        using var capture = new CapturingLoggerProvider();
        _diagA.Trace = (_, _) => _TraceAnswer(_TraceRun("run-1", at: 0));
        // A failing module logs a warning; that path must leave the id out too.
        _diagB.Trace = (_, _) => throw new InvalidOperationException("store is down");

        await using var app = await _StartAsync(services: services => services.AddSingleton<ILoggerProvider>(capture));
        using var client = app.GetTestClient();

        using var response = await _PostAsync(client, "/dash/api/trace", new { correlationId });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var records = capture.Records;
        // Information entries reach the capture, so the check below is not vacuous.
        records
            .Should()
            .Contain(r =>
                r.Level == LogLevel.Information
                && r.Category.StartsWith("Microsoft.AspNetCore.Hosting", StringComparison.Ordinal)
                && r.Message.Contains("/dash/api/trace", StringComparison.Ordinal)
            );
        records
            .Should()
            .Contain(r => r.Level == LogLevel.Warning && r.Message.Contains("diag-b", StringComparison.Ordinal));
        records
            .Where(static r => r.Level >= LogLevel.Information)
            .Should()
            .NotContain(r => r.Message.Contains(correlationId, StringComparison.Ordinal));
    }

    private const string _Id201 =
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
        + "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private static IEnumerable<(string Module, string Id, int Depth)> _TraceShape(JsonElement body) =>
        body.GetProperty("rows")
            .EnumerateArray()
            .Select(static row =>
                (
                    row.GetProperty("module").GetString()!,
                    row.GetProperty("target").GetProperty("id").GetString()!,
                    row.GetProperty("depth").GetInt32()
                )
            );

    private static Task<DashboardTraceAnswer> _TraceAnswer(params DashboardTraceItem[] items) =>
        Task.FromResult(new DashboardTraceAnswer { Items = items });

    private static Task<DashboardTraceAnswer> _TraceAnswer(DashboardTraceAnswer answer) => Task.FromResult(answer);

    private static DashboardTraceKey _RunKey(string id) => new() { Kind = DashboardTraceKeyKind.Run, Value = id };

    private static DashboardTraceKey _MessageKey(string id) =>
        new() { Kind = DashboardTraceKeyKind.Message, Value = id };

    private DashboardTraceItem _TraceRun(string id, int at) =>
        new()
        {
            Target = new ObjectRef
            {
                Module = "diag-a",
                Kind = "run",
                Id = id,
            },
            Store = "primary",
            StorageId = id,
            Title = $"Run {id}",
            At = _time.GetUtcNow().AddSeconds(at),
            CorrelationId = "ord-7",
            Key = _RunKey(id),
        };

    private DashboardTraceItem _TracePublished(string id, int at, string? causedByRun) =>
        new()
        {
            Target = new ObjectRef
            {
                Module = "diag-b",
                Kind = "published",
                Id = id,
            },
            Store = "primary",
            StorageId = id,
            Title = $"Message {id}",
            At = _time.GetUtcNow().AddSeconds(at),
            TimestampKind = DashboardTraceTimestampKind.Added,
            CorrelationId = "ord-7",
            Key = _MessageKey(id),
            Causation = causedByRun is null ? null : _RunKey(causedByRun),
        };

    private static Task<HttpResponseMessage> _PostAsync(HttpClient client, string path, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-Requested-With", "XMLHttpRequest");

        return _SendAndDisposeAsync(client, request);
    }

    private static async Task<HttpResponseMessage> _SendAndDisposeAsync(HttpClient client, HttpRequestMessage request)
    {
        using (request)
        {
            return await client.SendAsync(request, AbortToken);
        }
    }

    private static Task<IReadOnlyDictionary<string, DashboardCount>> _Counts(
        params (string Key, DashboardCount Count)[] counts
    )
    {
        return Task.FromResult<IReadOnlyDictionary<string, DashboardCount>>(
            counts.ToDictionary(c => c.Key, c => c.Count, StringComparer.Ordinal)
        );
    }

    private static JsonElement _ModuleSection(JsonElement body, string moduleId)
    {
        return body.GetProperty("modules")
            .EnumerateArray()
            .Single(m => string.Equals(m.GetProperty("id").GetString(), moduleId, StringComparison.Ordinal));
    }

    private static IEnumerable<string> _Strings(JsonElement array) =>
        array.EnumerateArray().Select(static item => item.GetString()!);

    private static (long Count, bool Capped) _Count(JsonElement counts, string key)
    {
        var count = counts.GetProperty(key);

        return (count.GetProperty("count").GetInt64(), count.GetProperty("capped").GetBoolean());
    }

    private async Task<WebApplication> _StartAsync(
        bool withDiagB = true,
        Action<IServiceCollection>? services = null,
        Action<HeadlessDashboardSetupBuilder>? configure = null,
        bool hostAuthentication = false,
        Action<HeadlessDashboardSetupBuilder>? auth = null
    )
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        builder.Services.AddSingleton<TimeProvider>(_time);
        builder.Services.AddSingleton(_diagA);
        builder.Services.AddSingleton(_diagB);
        builder
            .Services.AddAuthentication(TestAuthHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                TestAuthHandler.SchemeName,
                configureOptions: null
            );
        builder
            .Services.AddAuthorizationBuilder()
            .AddPolicy(_DiagBViewers, policy => policy.RequireRole("diag-b-viewer"));
        services?.Invoke(builder.Services);

        builder.Services.AddHeadlessDashboard(dashboard =>
        {
            if (auth is not null)
            {
                auth(dashboard);
            }
            else if (hostAuthentication)
            {
                dashboard.WithHostAuthentication();
            }
            else
            {
                dashboard.WithNoAuth();
            }

            dashboard.SetBasePath("/dash").AddModule<DiagAModule, DiagAModuleOptions>("diag-a");

            if (withDiagB)
            {
                dashboard.AddModule<DiagBModule, DiagBModuleOptions>("diag-b");
            }

            configure?.Invoke(dashboard);
        });

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapHeadlessDashboard();

        await app.StartAsync(AbortToken);

        return app;
    }

    // The session cookie is Secure, so the cookie handler issues and reads it only on an https request.
    private static HttpClient _HttpsClient(WebApplication app)
    {
        var client = app.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        return client;
    }

    // Signs the host user in for a session, as the browser does, then sends the request with the session cookie alone.
    private static async Task<HttpResponseMessage> _GetSignedInAsync(
        HttpClient client,
        string url,
        string roles,
        object? postBody = null
    )
    {
        using var exchange = new HttpRequestMessage(HttpMethod.Post, "/dash/api/auth/session");
        exchange.Headers.Add("X-Test-User", "operator");
        exchange.Headers.Add("X-Test-Roles", roles);
        exchange.Headers.Add("X-Requested-With", "XMLHttpRequest");
        using var signedIn = await client.SendAsync(exchange, AbortToken);
        signedIn.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var cookie = SetCookieHeaderValue
            .ParseList([.. signedIn.Headers.GetValues("Set-Cookie")])
            .Single(c => c.Name.Value!.StartsWith("__Host-headless-dashboard-", StringComparison.Ordinal));

        using var request = new HttpRequestMessage(postBody is null ? HttpMethod.Get : HttpMethod.Post, url);
        request.Headers.Add("Cookie", $"{cookie.Name}={cookie.Value}");

        if (postBody is not null)
        {
            request.Content = JsonContent.Create(postBody);
            request.Headers.Add("X-Requested-With", "XMLHttpRequest");
        }

        return await client.SendAsync(request, AbortToken);
    }

    private static async Task<JsonElement> _ReadAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync(AbortToken);
        using var document = JsonDocument.Parse(json);

        return document.RootElement.Clone();
    }

    private static Task<IReadOnlyList<DashboardAttentionItem>> _Items(params DashboardAttentionItem[] items)
    {
        return Task.FromResult<IReadOnlyList<DashboardAttentionItem>>(items);
    }

    private static DashboardAttentionItem _Item(string id, long count)
    {
        return new DashboardAttentionItem
        {
            Id = id,
            GroupKey = "failed-runs",
            Title = "Runs failed",
            Severity = DashboardAttentionSeverity.Critical,
            Count = count,
        };
    }

    private static DashboardObject _Run(string module, string id)
    {
        return new DashboardObject
        {
            Target = new ObjectRef
            {
                Module = module,
                Kind = "run",
                Id = id,
            },
            Title = $"Run {id}",
            Status = "Failed",
        };
    }

    /// <summary>A logger provider that keeps every entry with its category, level, and rendered message.</summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly Lock _lock = new();
        private readonly List<LogRecord> _records = [];

        public IReadOnlyList<LogRecord> Records
        {
            get
            {
                lock (_lock)
                {
                    return [.. _records];
                }
            }
        }

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

        public void Dispose() { }

        private void _Add(LogRecord record)
        {
            lock (_lock)
            {
                _records.Add(record);
            }
        }

        private sealed class CapturingLogger(CapturingLoggerProvider provider, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter
            )
            {
                provider._Add(new LogRecord(category, logLevel, formatter(state, exception)));
            }
        }
    }

    private sealed record LogRecord(string Category, LogLevel Level, string Message);
}
