// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Dashboard;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Time.Testing;
using Tests.Authentication;
using static Tests.Commands.CommandTestHost;

namespace Tests.Commands;

/// <summary>
/// The shell's command endpoints on a real host: it enforces the permission, the host's overrides, the channel, the
/// reason, the argument schema, and the confirmation for every module alike, and logs only what it accepted.
/// </summary>
public sealed class CommandEndpointTests : TestBase
{
    private const string _Viewer = "Bearer diag-viewer";
    private const string _Operator = "Bearer diag-operator";
    private const string _RunRoute = "";
    private const string _ConfirmRoute = "/confirm";
    private const string _ChunksRoute = "/chunks";

    private readonly FakeTimeProvider _time = ShellTestHost.NewTime();
    private readonly FakeRunStore _store = new();
    private readonly PermissionGate _gate = new();
    private readonly RecordingOperatorLog _log = new();

    public CommandEndpointTests()
    {
        _store.SeedFailed(200);
    }

    [Theory]
    [InlineData(_RunRoute)]
    [InlineData(_ConfirmRoute)]
    [InlineData(_ChunksRoute)]
    public async Task should_answer_403_and_log_nothing_when_a_viewer_calls_an_operate_command(string route)
    {
        await using var app = await _StartAsync();
        using var client = ShellTestHost.Client(app);
        var cookie = await ShellTestHost.SignInAsync(client, _Viewer, AbortToken);

        using var response = await _PostRouteAsync(client, route, cookie);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReadAsync(response, AbortToken))
            .GetProperty("detail")
            .GetString()
            .Should()
            .Be("Needs operate permission");
        _log.Entries.Should().BeEmpty();
        Module(app).Executed.Should().BeEmpty();
        Module(app).Chunks.Should().BeEmpty();
        _store.Get("run-000001").State.Should().Be(FakeRunState.Failed);
    }

    [Theory]
    [InlineData(_RunRoute)]
    [InlineData(_ConfirmRoute)]
    [InlineData(_ChunksRoute)]
    public async Task should_answer_400_and_log_nothing_when_an_operator_posts_without_the_requested_with_header(
        string route
    )
    {
        await using var app = await _StartAsync();
        using var client = ShellTestHost.Client(app);
        var cookie = await ShellTestHost.SignInAsync(client, _Operator, AbortToken);

        using var response = await _PostRouteAsync(client, route, cookie, requestedWith: false);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _log.Entries.Should().BeEmpty();
        Module(app).Executed.Should().BeEmpty();
        Module(app).Chunks.Should().BeEmpty();
        _store.Get("run-000001").State.Should().Be(FakeRunState.Failed);
    }

    [Fact]
    public async Task should_disable_the_command_with_the_operate_reason_when_a_viewer_lists_commands()
    {
        await using var app = await _StartAsync();
        using var client = ShellTestHost.Client(app);
        var cookie = await ShellTestHost.SignInAsync(client, _Viewer, AbortToken);

        var requeue = _Command(await _ListAsync(client, cookie), FakeCommandModule.Requeue);
        using var allowed = await GetAsync(client, $"{Base}/allowed?kind=run&id=run-000001", cookie, AbortToken);

        requeue.GetProperty("canRun").GetBoolean().Should().BeFalse();
        requeue.GetProperty("disabledReason").GetString().Should().Be("Needs operate permission");
        var answer = _Availability(await ReadAsync(allowed, AbortToken), "run-000001", FakeCommandModule.Requeue);
        answer.GetProperty("allowed").GetBoolean().Should().BeFalse();
        answer.GetProperty("disabledReason").GetString().Should().Be("Needs operate permission");
    }

    [Fact]
    public async Task should_answer_404_and_leave_the_command_out_of_the_list_when_the_host_disables_it()
    {
        await using var app = await _StartAsync(disabled: [FakeCommandModule.Requeue]);
        using var client = ShellTestHost.Client(app);
        var cookie = await ShellTestHost.SignInAsync(client, _Operator, AbortToken);

        using var response = await _RequeueAsync(client, cookie, ["run-000001"]);
        var ids = _Ids(await _ListAsync(client, cookie));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        ids.Should().NotContain(FakeCommandModule.Requeue).And.Contain(FakeCommandModule.Pause);
        _log.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task should_answer_400_naming_the_reason_field_when_a_reason_is_required_and_missing()
    {
        await using var app = await _StartAsync();
        using var client = ShellTestHost.Client(app);
        var cookie = await ShellTestHost.SignInAsync(client, _Operator, AbortToken);

        using var response = await PostAsync(
            client,
            $"{Base}/{FakeCommandModule.Requeue}",
            new { ids = new[] { "run-000001" }, confirmed = true },
            cookie,
            AbortToken
        );

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await ReadAsync(response, AbortToken);
        problem.GetProperty("errors").TryGetProperty("reason", out _).Should().BeTrue();
        problem.GetProperty("rules").GetProperty("reason").GetString().Should().Be("required");
        _log.Entries.Should().BeEmpty();
    }

    [Theory]
    [InlineData("""{"delayMinutes":"5"}""", "arguments.delayMinutes", "type")]
    [InlineData("{}", "arguments.delayMinutes", "required")]
    [InlineData("""{"delayMinutes":0}""", "arguments.delayMinutes", "minimum")]
    [InlineData("""{"delayMinutes":61}""", "arguments.delayMinutes", "maximum")]
    [InlineData("""{"delayMinutes":5,"priority":"urgent"}""", "arguments.priority", "enum")]
    public async Task should_answer_400_with_the_field_and_rule_when_an_argument_breaks_the_schema(
        string arguments,
        string field,
        string rule
    )
    {
        await using var app = await _StartAsync();
        using var client = ShellTestHost.Client(app);
        var cookie = await ShellTestHost.SignInAsync(client, _Operator, AbortToken);
        using var document = JsonDocument.Parse(arguments);

        using var response = await PostAsync(
            client,
            $"{Base}/{FakeCommandModule.RetryLater}",
            new { ids = new[] { "run-000001" }, arguments = document.RootElement },
            cookie,
            AbortToken
        );

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await ReadAsync(response, AbortToken);
        problem.GetProperty("errors").TryGetProperty(field, out _).Should().BeTrue();
        problem.GetProperty("rules").GetProperty(field).GetString().Should().Be(rule);
        Module(app).Executed.Should().BeEmpty();
    }

    [Fact]
    public async Task should_pass_the_validated_arguments_to_the_module_when_they_match_the_schema()
    {
        await using var app = await _StartAsync();
        using var client = ShellTestHost.Client(app);
        var cookie = await ShellTestHost.SignInAsync(client, _Operator, AbortToken);
        using var document = JsonDocument.Parse("""{"delayMinutes":15,"priority":"high"}""");

        using var response = await PostAsync(
            client,
            $"{Base}/{FakeCommandModule.RetryLater}",
            new { ids = new[] { "run-000001" }, arguments = document.RootElement },
            cookie,
            AbortToken
        );

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var request = Module(app).Executed.Should().ContainSingle().Which;
        request.Arguments["delayMinutes"].GetInt32().Should().Be(15);
        request.Arguments["priority"].GetString().Should().Be("high");
        request.Channel.Should().Be(DashboardChannels.Ui);
    }

    [Fact]
    public async Task should_answer_404_when_a_ui_only_command_is_called_without_the_ui_channel_header()
    {
        await using var app = await _StartAsync();
        using var client = ShellTestHost.Client(app);
        var cookie = await ShellTestHost.SignInAsync(client, _Operator, AbortToken);

        using var response = await PostAsync(
            client,
            $"{Base}/{FakeCommandModule.RetryLater}",
            new { ids = new[] { "run-000001" }, arguments = new { delayMinutes = 5 } },
            cookie,
            AbortToken,
            ui: false
        );

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        Module(app).Executed.Should().BeEmpty();
    }

    [Fact]
    public async Task should_leave_an_api_only_command_out_of_the_ui_list_and_run_it_for_an_api_call()
    {
        await using var app = await _StartAsync();
        using var client = ShellTestHost.Client(app);
        var cookie = await ShellTestHost.SignInAsync(client, _Viewer, AbortToken);

        var ids = _Ids(await _ListAsync(client, cookie));
        using var response = await PostAsync(
            client,
            $"{Base}/{FakeCommandModule.Export}",
            new { ids = new[] { "run-000001" } },
            cookie,
            AbortToken,
            ui: false
        );

        ids.Should().NotContain(FakeCommandModule.Export);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _log.Entries.Should().ContainSingle().Which.Channel.Should().Be(DashboardChannels.Api);
    }

    [Fact]
    public async Task should_run_the_command_and_log_one_entry_when_an_operator_requeues_by_ids()
    {
        await using var app = await _StartAsync();
        using var client = ShellTestHost.Client(app);
        var cookie = await ShellTestHost.SignInAsync(client, _Operator, AbortToken);

        using var response = await _RequeueAsync(client, cookie, ["run-000001", "run-000002", "run-000001"]);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = (await ReadAsync(response, AbortToken)).GetProperty("rows");
        rows.EnumerateArray()
            .Select(static row => (row.GetProperty("id").GetString(), row.GetProperty("outcome").GetString()))
            .Should()
            .Equal(("run-000001", "done"), ("run-000002", "done"));
        var entry = _log.Entries.Should().ContainSingle().Which;
        entry.Action.Should().Be(OperatorLogAction.Run);
        entry.Actor.Should().Be("diag-operator");
        entry.Channel.Should().Be(DashboardChannels.Ui);
        entry.Module.Should().Be("diag");
        entry.CommandId.Should().Be(FakeCommandModule.Requeue);
        entry.Ids.Should().Equal("run-000001", "run-000002");
        entry.Count.Should().Be(2);
        entry.Reason.Should().Be("stuck after deploy");
        entry.Done.Should().Be(2);
        entry.Timestamp.Should().Be(_time.GetUtcNow());
    }

    [Fact]
    public async Task should_answer_400_when_a_command_is_called_directly_with_a_filter()
    {
        await using var app = await _StartAsync();
        using var client = ShellTestHost.Client(app);
        var cookie = await ShellTestHost.SignInAsync(client, _Operator, AbortToken);

        using var response = await PostAsync(
            client,
            $"{Base}/{FakeCommandModule.Requeue}",
            new
            {
                filter = new { queue = "default" },
                reason = "x",
                confirmed = true,
            },
            cookie,
            AbortToken
        );

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadAsync(response, AbortToken))
            .GetProperty("rules")
            .GetProperty("filter")
            .GetString()
            .Should()
            .Be("operation");
        Module(app).Executed.Should().BeEmpty();
    }

    [Fact]
    public async Task should_disable_requeue_with_the_module_reason_and_return_its_refusal_when_called_on_a_running_run()
    {
        _store.SetState("run-000003", FakeRunState.Running);
        await using var app = await _StartAsync();
        using var client = ShellTestHost.Client(app);
        var cookie = await ShellTestHost.SignInAsync(client, _Operator, AbortToken);

        using var allowed = await GetAsync(
            client,
            $"{Base}/allowed?kind=run&id=run-000003&id=run-000004",
            cookie,
            AbortToken
        );
        using var response = await _RequeueAsync(client, cookie, ["run-000003"]);

        var answers = await ReadAsync(allowed, AbortToken);
        var running = _Availability(answers, "run-000003", FakeCommandModule.Requeue);
        running.GetProperty("allowed").GetBoolean().Should().BeFalse();
        running.GetProperty("disabledReason").GetString().Should().Be("Running - cancel instead");
        _Availability(answers, "run-000004", FakeCommandModule.Requeue)
            .GetProperty("allowed")
            .GetBoolean()
            .Should()
            .BeTrue();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var row = (await ReadAsync(response, AbortToken)).GetProperty("rows")[0];
        row.GetProperty("outcome").GetString().Should().Be("skipped");
        row.GetProperty("reason").GetString().Should().Be("Running - cancel instead");
        _log.Entries.Should().ContainSingle().Which.Skipped.Should().Be(1);
    }

    [Fact]
    public async Task should_answer_400_naming_confirmed_when_a_confirmed_command_is_called_unconfirmed()
    {
        await using var app = await _StartAsync();
        using var client = ShellTestHost.Client(app);
        var cookie = await ShellTestHost.SignInAsync(client, _Operator, AbortToken);

        using var response = await PostAsync(
            client,
            $"{Base}/{FakeCommandModule.Requeue}",
            new { ids = new[] { "run-000001" }, reason = "x" },
            cookie,
            AbortToken
        );

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadAsync(response, AbortToken))
            .GetProperty("rules")
            .GetProperty("confirmed")
            .GetString()
            .Should()
            .Be("confirm");
    }

    [Fact]
    public async Task should_require_the_typed_count_when_a_confirmed_command_names_more_than_100_rows()
    {
        await using var app = await _StartAsync();
        using var client = ShellTestHost.Client(app);
        var cookie = await ShellTestHost.SignInAsync(client, _Operator, AbortToken);
        var ids = Enumerable.Range(1, 101).Select(FakeRunStore.RunId).ToArray();

        using var clicked = await PostAsync(
            client,
            $"{Base}/{FakeCommandModule.Requeue}",
            new
            {
                ids,
                reason = "x",
                confirmed = true,
            },
            cookie,
            AbortToken
        );
        using var typed = await PostAsync(
            client,
            $"{Base}/{FakeCommandModule.Requeue}",
            new
            {
                ids,
                reason = "x",
                confirmed = true,
                typedCount = 101,
            },
            cookie,
            AbortToken
        );

        clicked.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadAsync(clicked, AbortToken))
            .GetProperty("rules")
            .GetProperty("typedCount")
            .GetString()
            .Should()
            .Be("typed");
        typed.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task should_declare_the_typed_threshold_and_no_confirmation_for_a_command_with_an_inverse()
    {
        await using var app = await _StartAsync();
        using var client = ShellTestHost.Client(app);
        var cookie = await ShellTestHost.SignInAsync(client, _Operator, AbortToken);

        var commands = await _ListAsync(client, cookie);
        using var pause = await PostAsync(
            client,
            $"{Base}/{FakeCommandModule.Pause}",
            new { ids = new[] { "run-000001" } },
            cookie,
            AbortToken
        );

        var requeue = _Command(commands, FakeCommandModule.Requeue);
        requeue.GetProperty("confirmation").GetString().Should().Be("confirm");
        requeue.GetProperty("typedConfirmationAbove").GetInt32().Should().Be(100);
        requeue.GetProperty("requiresReason").GetBoolean().Should().BeTrue();
        requeue.GetProperty("supportsFilter").GetBoolean().Should().BeTrue();
        requeue.GetProperty("canRun").GetBoolean().Should().BeTrue();
        var paused = _Command(commands, FakeCommandModule.Pause);
        paused.GetProperty("confirmation").GetString().Should().Be("none");
        paused.GetProperty("inverseCommandId").GetString().Should().Be(FakeCommandModule.Resume);
        pause.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task should_describe_the_argument_form_as_a_json_schema_subset()
    {
        await using var app = await _StartAsync();
        using var client = ShellTestHost.Client(app);
        var cookie = await ShellTestHost.SignInAsync(client, _Operator, AbortToken);

        var schema = _Command(await _ListAsync(client, cookie), FakeCommandModule.RetryLater).GetProperty("arguments");

        schema.GetProperty("type").GetString().Should().Be("object");
        schema
            .GetProperty("required")
            .EnumerateArray()
            .Select(static e => e.GetString())
            .Should()
            .Equal("delayMinutes");
        var delay = schema.GetProperty("properties").GetProperty("delayMinutes");
        delay.GetProperty("type").GetString().Should().Be("integer");
        delay.GetProperty("minimum").GetDouble().Should().Be(1);
        delay.GetProperty("maximum").GetDouble().Should().Be(60);
        schema
            .GetProperty("properties")
            .GetProperty("priority")
            .GetProperty("enum")
            .EnumerateArray()
            .Select(static e => e.GetString())
            .Should()
            .Equal("low", "high");
    }

    [Fact]
    public async Task should_refuse_a_capability_command_with_its_named_permission_when_the_host_policy_refuses()
    {
        _gate.Purge = false;
        await using var app = await _StartAsync();
        using var client = ShellTestHost.Client(app);
        var cookie = await ShellTestHost.SignInAsync(client, _Operator, AbortToken);

        using var response = await PostAsync(
            client,
            $"{Base}/{FakeCommandModule.Purge}",
            new { ids = new[] { "run-000001" }, confirmed = true },
            cookie,
            AbortToken
        );

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReadAsync(response, AbortToken))
            .GetProperty("detail")
            .GetString()
            .Should()
            .Be("Needs the diag.purge permission");
    }

    [Fact]
    public async Task should_serve_the_module_own_endpoints_beside_the_shell_command_endpoints()
    {
        await using var app = await _StartAsync();
        using var client = ShellTestHost.Client(app);
        var cookie = await ShellTestHost.SignInAsync(client, _Operator, AbortToken);

        using var items = await GetAsync(client, "/headless/api/diag/items", cookie, AbortToken);
        using var commands = await GetAsync(client, Base, cookie, AbortToken);
        using var unknown = await _RequeueAsync(client, cookie, ["run-000001"], command: "diag.nope");

        items.StatusCode.Should().Be(HttpStatusCode.OK);
        (await items.Content.ReadAsStringAsync(AbortToken)).Should().Be("items");
        commands.StatusCode.Should().Be(HttpStatusCode.OK);
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(_RunRoute)]
    [InlineData(_ConfirmRoute)]
    [InlineData(_ChunksRoute)]
    public async Task should_answer_401_when_a_command_is_called_without_a_session(string route)
    {
        await using var app = await _StartAsync();
        using var client = ShellTestHost.Client(app);

        using var response = await _PostRouteAsync(client, route, cookie: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _log.Entries.Should().BeEmpty();
        Module(app).Executed.Should().BeEmpty();
        Module(app).Chunks.Should().BeEmpty();
    }

    private Task<WebApplication> _StartAsync(string[]? disabled = null)
    {
        return CommandTestHost.StartAsync(
            new CommandTestHostOptions
            {
                Store = _store,
                Time = _time,
                Gate = _gate,
                Log = _log,
                DisabledIds = disabled ?? [],
            },
            AbortToken
        );
    }

    // A body each route would accept from an operator, so only the check under test can refuse the call.
    private Task<HttpResponseMessage> _PostRouteAsync(
        HttpClient client,
        string route,
        string? cookie,
        bool requestedWith = true
    )
    {
        object body = route switch
        {
            _RunRoute => new
            {
                ids = new[] { "run-000001" },
                reason = "stuck after deploy",
                confirmed = true,
            },
            _ConfirmRoute => new { filter = new { queue = "default" }, reason = "stuck after deploy" },
            _ => new { token = "not-a-token", confirmed = true },
        };

        return PostAsync(
            client,
            $"{Base}/{FakeCommandModule.Requeue}{route}",
            body,
            cookie,
            AbortToken,
            requestedWith: requestedWith
        );
    }

    private Task<HttpResponseMessage> _RequeueAsync(
        HttpClient client,
        string? cookie,
        string[] ids,
        string command = FakeCommandModule.Requeue
    )
    {
        return PostAsync(
            client,
            $"{Base}/{command}",
            new
            {
                ids,
                reason = "stuck after deploy",
                confirmed = true,
            },
            cookie,
            AbortToken
        );
    }

    private async Task<JsonElement> _ListAsync(HttpClient client, string cookie)
    {
        using var response = await GetAsync(client, Base, cookie, AbortToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return (await ReadAsync(response, AbortToken)).GetProperty("commands");
    }

    private static string?[] _Ids(JsonElement commands)
    {
        return [.. commands.EnumerateArray().Select(static c => c.GetProperty("id").GetString())];
    }

    private static JsonElement _Command(JsonElement commands, string id)
    {
        return commands
            .EnumerateArray()
            .Single(c => string.Equals(c.GetProperty("id").GetString(), id, StringComparison.Ordinal));
    }

    private static JsonElement _Availability(JsonElement answer, string objectId, string commandId)
    {
        return answer
            .GetProperty("objects")
            .EnumerateArray()
            .Single(o => string.Equals(o.GetProperty("id").GetString(), objectId, StringComparison.Ordinal))
            .GetProperty("commands")
            .EnumerateArray()
            .Single(c => string.Equals(c.GetProperty("id").GetString(), commandId, StringComparison.Ordinal));
    }
}
