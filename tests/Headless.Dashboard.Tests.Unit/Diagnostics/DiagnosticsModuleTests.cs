// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Security.Claims;
using Headless.Dashboard;
using Headless.Hosting;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Tests.Authentication;

namespace Tests.Diagnostics;

/// <summary>
/// The built-in diagnostic module: when it serves, what it registers, and each capability it exercises over its
/// in-process store.
/// </summary>
public sealed class DiagnosticsModuleTests : TestBase
{
    private const string _Id = DiagnosticsOptions.DefaultId;
    private const string _Fail = _Id + ".fail";
    private const string _Requeue = _Id + ".requeue";
    private const string _AddNote = _Id + ".add-note";

    private static readonly ClaimsPrincipal _User = new(new ClaimsIdentity([new(ClaimTypes.Name, "ops")], "test"));

    private readonly FakeTimeProvider _time = ShellTestHost.NewTime();

    [Fact]
    public async Task should_answer_404_and_stay_out_of_the_manifest_when_outside_development_and_not_enabled()
    {
        await using var app = await _StartAsync(Environments.Production, dashboard => dashboard.AddDiagnostics());
        using var client = _Client(app);

        using var api = await _GetAsync(client, $"/headless/api/{_Id}/runs");
        using var asset = await _GetAsync(client, $"/headless/m/{_Id}/index.js");
        using var command = await _PostAsync(
            client,
            $"/headless/api/{_Id}/commands/{_Fail}",
            """{"ids":["run-000001"]}"""
        );

        api.StatusCode.Should().Be(HttpStatusCode.NotFound);
        asset.StatusCode.Should().Be(HttpStatusCode.NotFound);
        command.StatusCode.Should().Be(HttpStatusCode.NotFound);
        _Manifest(app).Modules.Should().NotContain(module => string.Equals(module.Id, _Id, StringComparison.Ordinal));
    }

    [Fact]
    public async Task should_appear_with_its_nav_routes_and_api_when_the_environment_is_development()
    {
        await using var app = await _StartAsync(Environments.Development, dashboard => dashboard.AddDiagnostics());
        using var client = _Client(app);

        var module = _Manifest(app)
            .Modules.Should()
            .ContainSingle(module => string.Equals(module.Id, _Id, StringComparison.Ordinal))
            .Which;
        module.IsRefused.Should().BeFalse();
        module.Manifest!.Nav.Should().NotBeEmpty();
        module.Manifest.Routes.Should().NotBeEmpty();

        using var asset = await _GetAsync(client, $"/headless/m/{_Id}/{module.Manifest.Entry}");
        using var api = await _GetAsync(client, $"/headless/api/{_Id}/runs");

        asset.StatusCode.Should().Be(HttpStatusCode.OK);
        api.StatusCode.Should().Be(HttpStatusCode.OK);
        (await _ReadAsync(api)).GetProperty("count").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task should_serve_outside_development_when_the_host_enables_it()
    {
        await using var app = await _StartAsync(
            Environments.Production,
            dashboard => dashboard.AddDiagnostics(diagnostics => diagnostics.Enabled = true)
        );
        using var client = _Client(app);

        using var api = await _GetAsync(client, $"/headless/api/{_Id}/runs");

        api.StatusCode.Should().Be(HttpStatusCode.OK);
        _Manifest(app)
            .Modules.Should()
            .ContainSingle(module => string.Equals(module.Id, _Id, StringComparison.Ordinal));
    }

    [Fact]
    public async Task should_keep_each_instance_apart_when_a_second_instance_is_added_under_another_id()
    {
        await using var app = await _StartAsync(
            Environments.Development,
            dashboard => dashboard.AddDiagnostics().AddDiagnostics(diagnostics => diagnostics.Instance("diag-b"))
        );
        using var client = _Client(app);
        app.Services.GetRequiredKeyedService<DiagnosticsStore>("diag-b").AddRun();

        using var first = await _GetAsync(client, $"/headless/api/{_Id}/runs");
        using var second = await _GetAsync(client, "/headless/api/diag-b/runs");

        (await _ReadAsync(first)).GetProperty("count").GetInt32().Should().Be(0);
        (await _ReadAsync(second)).GetProperty("count").GetInt32().Should().Be(1);

        var manifest = _Manifest(app);
        manifest.Modules.Select(static module => module.Id).Should().Equal(_Id, "diag-b");
        var renamed = manifest.Modules.Single(static module =>
            string.Equals(module.Id, "diag-b", StringComparison.Ordinal)
        );
        renamed.IsRefused.Should().BeFalse();
        renamed
            .Manifest!.Nav.Should()
            .NotBeEmpty()
            .And.OnlyContain(entry => entry.Id.StartsWith("diag-b.", StringComparison.Ordinal));
        renamed.ServerCommands.Select(static command => command.Id).Should().Contain("diag-b.requeue");
    }

    [Fact]
    public async Task should_register_no_hosted_service_or_provisioner_when_the_module_is_added()
    {
        var without = _Registrations(static dashboard => dashboard.WithNoAuth());
        var with = _Registrations(static dashboard => dashboard.WithNoAuth().AddDiagnostics());

        _Count<IHostedService>(with).Should().Be(_Count<IHostedService>(without));
        _Count<IResourceProvisioner>(with).Should().Be(_Count<IResourceProvisioner>(without));
        _Count<IResourceProvisionerSource>(with).Should().Be(_Count<IResourceProvisionerSource>(without));

        // The only store is the in-process one.
        await using var app = await _StartAsync(Environments.Development, dashboard => dashboard.AddDiagnostics());
        app.Services.GetRequiredKeyedService<DiagnosticsStore>(_Id).Should().BeOfType<DiagnosticsStore>();
    }

    [Fact]
    public async Task should_refuse_operator_actions_with_403_when_the_user_may_only_view_the_module()
    {
        var store = new DiagnosticsStore(_time);
        var run = store.AddRun().Id;
        store.Fail(run, "boom");
        await using var app = await _StartHostModeAsync(store);
        using var client = ShellTestHost.Client(app);
        var viewer = await ShellTestHost.SignInAsync(client, "Bearer diag-viewer", AbortToken);

        using var list = await _GetAsync(client, $"/headless/api/{_Id}/runs", viewer);
        using var requeue = await _PostAsync(client, _CommandPath(_Requeue), _RequeueBody(run), viewer);
        using var add = await _PostAsync(client, $"/headless/api/{_Id}/runs", "{}", viewer);

        list.StatusCode.Should().Be(HttpStatusCode.OK);
        requeue.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        add.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        store.Find(run)!.Status.Should().Be(DiagnosticsRunStatus.Failed);
    }

    [Fact]
    public async Task should_requeue_a_failed_run_when_the_user_may_operate_the_module()
    {
        var store = new DiagnosticsStore(_time);
        var run = store.AddRun().Id;
        store.Fail(run, "boom");
        await using var app = await _StartHostModeAsync(store);
        using var client = ShellTestHost.Client(app);
        var operatorCookie = await ShellTestHost.SignInAsync(client, "Bearer diag-operator", AbortToken);

        using var requeue = await _PostAsync(client, _CommandPath(_Requeue), _RequeueBody(run), operatorCookie);

        requeue.StatusCode.Should().Be(HttpStatusCode.OK);
        (await _ReadAsync(requeue)).GetProperty("rows")[0].GetProperty("outcome").GetString().Should().Be("done");
        store.Find(run)!.Status.Should().Be(DiagnosticsRunStatus.Queued);
    }

    [Fact]
    public async Task should_apply_each_instances_own_view_policy_when_two_instances_are_added()
    {
        await using var app = await _StartHostModeAsync(new DiagnosticsStore(_time));
        using var client = ShellTestHost.Client(app);
        var viewer = await ShellTestHost.SignInAsync(client, "Bearer diag-viewer", AbortToken);

        using var own = await _GetAsync(client, $"/headless/api/{_Id}/runs", viewer);
        using var other = await _GetAsync(client, "/headless/api/diag-b/runs", viewer);

        own.StatusCode.Should().Be(HttpStatusCode.OK);
        other.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task should_publish_a_local_event_and_report_a_higher_version_when_a_run_is_failed()
    {
        var store = new DiagnosticsStore(_time);
        var run = store.AddRun();
        var publisher = new RecordingPublisher();
        var module = _Module(store, publisher);
        var cursor = await module.GetStartCursorAsync(AbortToken);

        var result = await module.ExecuteAsync(_Command(_Fail, run.Id), AbortToken);
        var batch = await module.ReadChangesAsync(cursor, AbortToken);

        result.Rows.Should().ContainSingle().Which.Outcome.Should().Be(DashboardRowOutcome.Done);
        var published = publisher.Published.Should().ContainSingle().Which;
        published
            .Target.Should()
            .Be(
                new ObjectRef
                {
                    Module = _Id,
                    Kind = "run",
                    Id = run.Id,
                }
            );
        published.AffectsAttention.Should().BeTrue();
        var read = batch.Changes.Should().ContainSingle().Which;
        read.Version.Should().Be(published.Version);
        _Sequence(read.Version).Should().BeGreaterThan(run.Version).And.BeGreaterThan(_Sequence(cursor));
        _Sequence(batch.Cursor).Should().Be(_Sequence(read.Version));
        read.Projection!.Value.GetProperty("status").GetString().Should().Be("Failed");
    }

    [Fact]
    public async Task should_read_a_change_made_through_another_node_when_two_nodes_share_one_store()
    {
        var store = new DiagnosticsStore(_time);
        var run = store.AddRun().Id;
        var publisherA = new RecordingPublisher();
        var nodeA = _Module(store, publisherA);
        var nodeB = _Module(store, new RecordingPublisher());
        var cursor = await nodeA.GetStartCursorAsync(AbortToken);

        await nodeB.ExecuteAsync(_Command(_Fail, run), AbortToken);
        var batch = await nodeA.ReadChangesAsync(cursor, AbortToken);
        var again = await nodeA.ReadChangesAsync(batch.Cursor, AbortToken);

        publisherA.Published.Should().BeEmpty();
        batch.Changes.Should().ContainSingle().Which.Target.Id.Should().Be(run);
        again.Changes.Should().BeEmpty();
        again.Cursor.Should().Be(batch.Cursor);
    }

    [Fact]
    public async Task should_requeue_only_runs_failed_before_confirm_and_no_run_twice_when_an_operation_runs_again()
    {
        var store = new DiagnosticsStore(_time);
        var inScope = _SeedFailed(store, 5, "boom");
        var module = _Module(store, new RecordingPublisher());
        var filter = _Filter("""{"status":"Failed"}""");

        var scope = await module.PrepareAsync(_Scope(filter), AbortToken);
        var late = _SeedFailed(store, 1, "boom")[0];
        var first = await _RunOperationAsync(module, scope, filter, chunkSize: 2);
        var second = await _RunOperationAsync(module, scope, filter, chunkSize: 2);
        var reconfirmed = await module.PrepareAsync(_Scope(filter), AbortToken);

        scope.Count.Should().Be(5);
        scope.Sample.Should().HaveCount(5);
        first
            .Where(static row => row.Outcome == DashboardRowOutcome.Done)
            .Select(static row => row.Id)
            .Should()
            .BeEquivalentTo(inScope);
        second.Should().NotContain(static row => row.Outcome == DashboardRowOutcome.Done);
        store.Find(late)!.Status.Should().Be(DiagnosticsRunStatus.Failed);
        inScope.Should().OnlyContain(id => store.Find(id)!.Status == DiagnosticsRunStatus.Queued);
        reconfirmed.Count.Should().Be(1);
    }

    [Fact]
    public async Task should_leave_the_remaining_runs_untouched_when_an_operation_stops_after_one_chunk()
    {
        var store = new DiagnosticsStore(_time);
        var runs = _SeedFailed(store, 5, "boom");
        var module = _Module(store, new RecordingPublisher());
        var filter = _Filter("""{"error":"boom"}""");
        var scope = await module.PrepareAsync(_Scope(filter), AbortToken);

        var chunk = await module.RunChunkAsync(_Chunk(scope, filter, cursor: null, chunkSize: 2), AbortToken);

        chunk.Rows.Should().HaveCount(2).And.OnlyContain(static row => row.Outcome == DashboardRowOutcome.Done);
        chunk.NextCursor.Should().NotBeNull();
        runs.Count(id => store.Find(id)!.Status == DiagnosticsRunStatus.Failed).Should().Be(3);
    }

    [Fact]
    public async Task should_refuse_requeue_with_a_reason_when_the_run_is_running()
    {
        var store = new DiagnosticsStore(_time);
        var run = store.AddRun().Id;
        store.Start(run);
        var module = _Module(store, new RecordingPublisher());
        var target = new ObjectRef
        {
            Module = _Id,
            Kind = "run",
            Id = run,
        };

        var allowed = await module.GetAllowedCommandsAsync(
            new DashboardAllowedCommandsRequest { Targets = [target], User = _User },
            AbortToken
        );
        var result = await module.ExecuteAsync(_Command(_Requeue, run, reason: "retry"), AbortToken);

        var requeue = allowed
            .Should()
            .ContainSingle(answer => string.Equals(answer.CommandId, _Requeue, StringComparison.Ordinal))
            .Which;
        requeue.Allowed.Should().BeFalse();
        requeue.DisabledReason.Should().Be("Running - cancel instead");
        allowed
            .Single(answer => string.Equals(answer.CommandId, _Fail, StringComparison.Ordinal))
            .Allowed.Should()
            .BeTrue();
        var row = result.Rows.Should().ContainSingle().Which;
        row.Outcome.Should().Be(DashboardRowOutcome.Skipped);
        row.Reason.Should().Be("Running - cancel instead");
        store.Find(run)!.Status.Should().Be(DiagnosticsRunStatus.Running);
    }

    [Fact]
    public void should_declare_fail_requeue_and_add_note_with_their_rules_when_the_module_is_built()
    {
        var module = _Module(new DiagnosticsStore(_time), new RecordingPublisher());

        var fail = module.Commands.Single(static command => string.Equals(command.Id, _Fail, StringComparison.Ordinal));
        var requeue = module.Commands.Single(static command =>
            string.Equals(command.Id, _Requeue, StringComparison.Ordinal)
        );
        var note = module.Commands.Single(static command =>
            string.Equals(command.Id, _AddNote, StringComparison.Ordinal)
        );

        fail.InverseCommandId.Should().Be(_Requeue);
        fail.RequiresReason.Should().BeFalse();
        requeue.Confirmation.Should().Be(DashboardConfirmation.Confirm);
        requeue.RequiresReason.Should().BeTrue();
        var text = note.Arguments.Should().ContainSingle().Which;
        text.Name.Should().Be("text");
        text.Kind.Should().Be(DashboardArgumentKind.Text);
        text.Required.Should().BeTrue();
        text.MaxLength.Should().Be(500);
        module.Commands.Should().OnlyContain(static command => command.ObjectKind == "run");
    }

    [Fact]
    public async Task should_add_the_note_to_the_timeline_when_add_note_runs()
    {
        var store = new DiagnosticsStore(_time);
        var run = store.AddRun().Id;
        var module = _Module(store, new RecordingPublisher());
        var request = _Command(_AddNote, run) with
        {
            Arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["text"] = JsonSerializer.SerializeToElement("checked the logs"),
            },
        };

        await module.ExecuteAsync(request, AbortToken);

        store
            .Find(run)!
            .Timeline.Should()
            .ContainSingle(entry => entry.Kind == DiagnosticsRunEventKind.Note)
            .Which.Text.Should()
            .Be("checked the logs");
    }

    [Fact]
    public async Task should_group_failed_runs_by_error_message_when_needs_you_asks()
    {
        var store = new DiagnosticsStore(_time);
        _SeedFailed(store, 2, "timeout");
        var single = _SeedFailed(store, 1, "disk full")[0];
        store.AddRun();
        var module = _Module(store, new RecordingPublisher());

        var items = await module.GetAttentionAsync(new DashboardAttentionRequest { User = _User }, AbortToken);

        items.Select(static item => (item.GroupKey, item.Count)).Should().Equal(("timeout", 2L), ("disk full", 1L));
        items.Should().OnlyContain(static item => item.Severity == DashboardAttentionSeverity.Critical);
        items[0].Target.Should().BeNull();
        items[1].Target!.Id.Should().Be(single);
    }

    [Fact]
    public async Task should_compare_each_count_with_an_hour_ago_when_overview_asks()
    {
        var store = new DiagnosticsStore(_time);
        var run = store.AddRun().Id;
        store.Start(run);
        _time.Advance(TimeSpan.FromHours(2));
        store.Fail(run, "boom");
        var module = _Module(store, new RecordingPublisher());

        var figures = await module.GetOverviewAsync(new DashboardOverviewRequest { User = _User }, AbortToken);

        figures
            .Select(static figure => (figure.Label, figure.Value, figure.Comparison!.Value))
            .Should()
            .Equal(("Failed", 1d, 0d), ("Running", 0d, 1d), ("Succeeded", 0d, 0d));
    }

    [Fact]
    public async Task should_redact_secret_fields_and_headers_when_a_payload_is_formatted()
    {
        var store = new DiagnosticsStore(_time);
        var run = store.AddRun();
        var module = _Module(store, new RecordingPublisher());
        var target = new ObjectRef
        {
            Module = _Id,
            Kind = "run",
            Id = run.Id,
        };

        var raw = await module.GetPayloadAsync(
            new DashboardPayloadRequest { Target = target, User = _User },
            AbortToken
        );
        var formatted = await module.FormatAsync(
            new DashboardPayloadFormatRequest
            {
                Target = target,
                Payload = raw!,
                User = _User,
            },
            AbortToken
        );

        raw!.Content.Should().Contain("diag-key-");
        formatted.Content.Should().NotContain("diag-key-").And.Contain("***");
        formatted.Headers["x-api-key"].Should().Be("***");
        formatted.Headers["x-trace-id"].Should().Be(run.TraceId);
        module.ObjectKinds.Should().Equal("run");
    }

    [Fact]
    public async Task should_relate_linked_runs_and_runs_of_the_same_trace_when_related_objects_are_asked()
    {
        var store = new DiagnosticsStore(_time);
        var traceId = new string('a', 32);
        var linked = store.AddRun(traceId).Id;
        var sibling = store.AddRun(traceId).Id;
        var foreign = new ObjectRef
        {
            Module = "jobs",
            Kind = "run",
            Id = "job-1",
        };
        store.Link(linked, foreign);
        var module = _Module(store, new RecordingPublisher());

        var forForeign = await module.GetRelatedAsync(
            new DashboardRelatedRequest { Target = foreign, User = _User },
            AbortToken
        );
        var forOwn = await module.GetRelatedAsync(
            new DashboardRelatedRequest
            {
                Target = new ObjectRef
                {
                    Module = _Id,
                    Kind = "run",
                    Id = linked,
                },
                User = _User,
            },
            AbortToken
        );

        var group = forForeign.Should().ContainSingle().Which;
        group.Id.Should().Be(_Id + ".linked-runs");
        group.Items.Should().ContainSingle().Which.Target.Id.Should().Be(linked);
        forOwn.Should().ContainSingle().Which.Items.Should().ContainSingle().Which.Target.Id.Should().Be(sibling);
        module.GroupIds.Should().Equal(_Id + ".linked-runs", _Id + ".same-trace");
    }

    [Fact]
    public async Task should_find_runs_by_id_and_by_trace_id_when_the_palette_looks_one_up()
    {
        var store = new DiagnosticsStore(_time);
        var traceId = new string('b', 32);
        var run = store.AddRun(traceId);
        store.AddRun();
        var module = _Module(store, new RecordingPublisher());

        var byId = await module.ResolveAsync(new DashboardResolveRequest { Query = run.Id, User = _User }, AbortToken);
        var byTrace = await module.FindByTraceAsync(
            new DashboardTraceRequest { TraceId = traceId, User = _User },
            AbortToken
        );
        var none = await module.ResolveAsync(new DashboardResolveRequest { Query = "run", User = _User }, AbortToken);

        byId.Should().ContainSingle().Which.TraceId.Should().Be(traceId);
        byTrace.Should().ContainSingle().Which.Target.Id.Should().Be(run.Id);
        none.Should().BeEmpty();
    }

    private DiagnosticsModule _Module(DiagnosticsStore store, IDashboardEventPublisher publisher)
    {
        return new DiagnosticsModule(_Id, store, publisher, _time);
    }

    private static string[] _SeedFailed(DiagnosticsStore store, int count, string error)
    {
        var ids = new string[count];

        for (var i = 0; i < count; i++)
        {
            ids[i] = store.AddRun().Id;
            store.Fail(ids[i], error);
        }

        return ids;
    }

    private static DashboardCommandRequest _Command(string commandId, string id, string? reason = null)
    {
        return new DashboardCommandRequest
        {
            CommandId = commandId,
            Ids = [id],
            Reason = reason,
            Channel = DashboardChannels.Ui,
            User = _User,
        };
    }

    private static JsonElement _Filter(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static DashboardOperationScopeRequest _Scope(JsonElement filter)
    {
        return new DashboardOperationScopeRequest
        {
            CommandId = _Requeue,
            Filter = filter,
            CountLimit = 100_000,
            SampleSize = 5,
            User = _User,
        };
    }

    private static DashboardOperationChunkRequest _Chunk(
        DashboardOperationScope scope,
        JsonElement filter,
        string? cursor,
        int chunkSize
    )
    {
        return new DashboardOperationChunkRequest
        {
            CommandId = _Requeue,
            Filter = filter,
            HighWaterMark = scope.HighWaterMark,
            Cursor = cursor,
            ChunkSize = chunkSize,
            Reason = "retry",
            User = _User,
        };
    }

    private async Task<List<DashboardRowResult>> _RunOperationAsync(
        DiagnosticsModule module,
        DashboardOperationScope scope,
        JsonElement filter,
        int chunkSize
    )
    {
        var rows = new List<DashboardRowResult>();
        string? cursor = null;

        // Bounded: a cursor that never ends would otherwise hang the test.
        for (var i = 0; i < 100; i++)
        {
            var chunk = await module.RunChunkAsync(_Chunk(scope, filter, cursor, chunkSize), AbortToken);
            rows.AddRange(chunk.Rows);
            cursor = chunk.NextCursor;

            if (cursor is null)
            {
                return rows;
            }
        }

        throw new InvalidOperationException("The Operation did not finish within 100 chunks.");
    }

    private static long _Sequence(string version) => long.Parse(version, CultureInfo.InvariantCulture);

    private static int _Count<TService>(IServiceCollection services)
    {
        return services.Count(static descriptor => descriptor.ServiceType == typeof(TService));
    }

    private static ServiceCollection _Registrations(Action<HeadlessDashboardSetupBuilder> configure)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new HostingEnvironment { EnvironmentName = Environments.Development });
        services.AddHeadlessDashboard(configure);

        return services;
    }

    private static MergedManifest _Manifest(WebApplication app)
    {
        return app.Services.GetRequiredService<DashboardManifestProvider>().Manifest;
    }

    private static string _CommandPath(string commandId) => $"/headless/api/{_Id}/commands/{commandId}";

    private static string _RequeueBody(string id) => $$"""{"ids":["{{id}}"],"reason":"retry","confirmed":true}""";

    private Task<WebApplication> _StartHostModeAsync(DiagnosticsStore store)
    {
        return ShellTestHost.StartAsync(
            dashboard =>
                dashboard
                    .WithHostAuthentication(host => host.ViewPolicy = ShellTestHost.OpsPolicy)
                    .AddDiagnostics(diagnostics =>
                    {
                        diagnostics.Enabled = true;
                        diagnostics.ViewPolicy = ShellTestHost.DiagViewersPolicy;
                        diagnostics.OperatePolicy = ShellTestHost.DiagOperatorsPolicy;
                        diagnostics.UseStore(store);
                    })
                    .AddDiagnostics(diagnostics =>
                    {
                        diagnostics.Instance("diag-b");
                        diagnostics.Enabled = true;
                        diagnostics.ViewPolicy = ShellTestHost.OpsPolicy;
                    }),
            new ShellTestHostOptions { Time = _time },
            AbortToken
        );
    }

    private async Task<WebApplication> _StartAsync(
        string environment,
        Action<HeadlessDashboardSetupBuilder> configureModules
    )
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<TimeProvider>(_time);
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddAuthentication();
        builder.Services.AddAuthorization();
        builder.Services.AddHeadlessDashboard(dashboard => configureModules(dashboard.WithNoAuth()));

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapHeadlessDashboard();

        try
        {
            await app.StartAsync(AbortToken);
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }

        return app;
    }

    private static HttpClient _Client(WebApplication app)
    {
        var client = app.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");
        return client;
    }

    private Task<HttpResponseMessage> _GetAsync(HttpClient client, string path, string? cookie = null)
    {
        return ShellTestHost.SendAsync(client, HttpMethod.Get, path, AbortToken, cookie: cookie);
    }

    private async Task<HttpResponseMessage> _PostAsync(
        HttpClient client,
        string path,
        string json,
        string? cookie = null
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative))
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");

        if (cookie is not null)
        {
            request.Headers.TryAddWithoutValidation("Cookie", cookie);
        }

        return await client.SendAsync(request, AbortToken);
    }

    private async Task<JsonElement> _ReadAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(AbortToken));
        return document.RootElement.Clone();
    }

    /// <summary>Keeps every change a module publishes, standing in for the node's live streams.</summary>
    private sealed class RecordingPublisher : IDashboardEventPublisher
    {
        private readonly Lock _lock = new();
        private readonly List<DashboardChange> _published = [];

        public IReadOnlyList<DashboardChange> Published
        {
            get
            {
                lock (_lock)
                {
                    return [.. _published];
                }
            }
        }

        public void Publish(DashboardChange change)
        {
            lock (_lock)
            {
                _published.Add(change);
            }
        }
    }
}
