// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Dashboard;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Time.Testing;
using Tests.Authentication;
using static Tests.Commands.CommandTestHost;

namespace Tests.Commands;

/// <summary>
/// Operations on a real host: the confirm step freezes and signs the scope, and the browser drives chunks that each
/// check the token and the permission again, touch no row twice, and leave rows that entered the state later alone.
/// </summary>
public sealed class OperationTests : TestBase
{
    private const string _Operator = "Bearer diag-operator";
    private const string _Reason = "incident 42";

    private readonly FakeTimeProvider _time = ShellTestHost.NewTime();
    private readonly FakeRunStore _store = new();
    private readonly PermissionGate _gate = new();
    private readonly RecordingOperatorLog _log = new();

    [Fact]
    public async Task should_answer_the_exact_count_five_sample_rows_and_a_token_when_confirming_5000_failed_rows()
    {
        _store.SeedFailed(5_000);
        await using var app = await _StartAsync();
        using var client = ShellTestHost.Client(app);
        var cookie = await ShellTestHost.SignInAsync(client, _Operator, AbortToken);

        var scope = await _ConfirmAsync(client, cookie);

        scope.GetProperty("count").GetInt64().Should().Be(5_000);
        scope.GetProperty("sample").GetArrayLength().Should().Be(5);
        scope.GetProperty("sample")[0].GetProperty("target").GetProperty("id").GetString().Should().Be("run-000001");
        scope.GetProperty("token").GetString().Should().NotBeNullOrEmpty();
        scope.GetProperty("confirmation").GetString().Should().Be("typed");
        scope.GetProperty("chunkSize").GetInt32().Should().Be(500);
        scope.GetProperty("expiresAt").GetDateTimeOffset().Should().Be(_time.GetUtcNow().AddHours(1));
        _store.All().Should().OnlyContain(static run => run.State == FakeRunState.Failed);
    }

    [Fact]
    public async Task should_refuse_the_confirm_with_the_narrow_the_filter_reason_when_more_than_100000_rows_match()
    {
        _store.SeedFailed(100_001);
        await using var app = await _StartAsync();
        using var client = ShellTestHost.Client(app);
        var cookie = await ShellTestHost.SignInAsync(client, _Operator, AbortToken);

        using var response = await _PostConfirmAsync(client, cookie, FakeCommandModule.Requeue);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await ReadAsync(response, AbortToken);
        problem.GetProperty("rules").GetProperty("filter").GetString().Should().Be("maxCount");
        problem.GetProperty("detail").GetString().Should().Contain("Narrow the filter");
        _log.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task should_refuse_a_tampered_token_at_the_next_chunk()
    {
        _store.SeedFailed(10);
        await using var app = await _StartAsync();
        using var client = ShellTestHost.Client(app);
        var cookie = await ShellTestHost.SignInAsync(client, _Operator, AbortToken);
        var token = (await _ConfirmAsync(client, cookie)).GetProperty("token").GetString()!;
        var middle = token.Length / 2;
        var tampered = token[..middle] + (token[middle] == 'A' ? 'B' : 'A') + token[(middle + 1)..];

        using var response = await _PostChunkAsync(
            client,
            cookie,
            FakeCommandModule.Requeue,
            tampered,
            cursor: null,
            10
        );

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadAsync(response, AbortToken))
            .GetProperty("rules")
            .GetProperty("token")
            .GetString()
            .Should()
            .Be("invalid");
        Module(app).Chunks.Should().BeEmpty();
    }

    [Fact]
    public async Task should_refuse_an_expired_token_at_the_next_chunk()
    {
        _store.SeedFailed(10);
        await using var app = await _StartAsync();
        using var client = ShellTestHost.Client(app);
        var cookie = await ShellTestHost.SignInAsync(client, _Operator, AbortToken);
        var token = (await _ConfirmAsync(client, cookie)).GetProperty("token").GetString()!;

        _time.Advance(TimeSpan.FromHours(1));
        using var response = await _PostChunkAsync(client, cookie, FakeCommandModule.Requeue, token, cursor: null, 10);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadAsync(response, AbortToken))
            .GetProperty("rules")
            .GetProperty("token")
            .GetString()
            .Should()
            .Be("expired");
        Module(app).Chunks.Should().BeEmpty();
    }

    [Fact]
    public async Task should_refuse_a_token_used_by_another_operator()
    {
        _store.SeedFailed(10);
        await using var app = await _StartAsync();
        using var client = ShellTestHost.Client(app);
        var confirmer = await ShellTestHost.SignInAsync(client, _Operator, AbortToken);
        var other = await ShellTestHost.SignInAsync(client, "Bearer ops", AbortToken);
        var token = (await _ConfirmAsync(client, confirmer)).GetProperty("token").GetString()!;

        using var response = await _PostChunkAsync(client, other, FakeCommandModule.Requeue, token, cursor: null, 10);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Module(app).Chunks.Should().BeEmpty();
    }

    [Fact]
    public async Task should_walk_the_scope_to_completion_in_chunks_of_500_with_per_row_outcomes()
    {
        _store.SeedFailed(5_000);
        await using var app = await _StartAsync();
        using var client = ShellTestHost.Client(app);
        var cookie = await ShellTestHost.SignInAsync(client, _Operator, AbortToken);
        var scope = await _ConfirmAsync(client, cookie);

        var chunks = await _RunAllAsync(client, cookie, scope);

        chunks.Take(10).Should().OnlyContain(static chunk => chunk.GetProperty("rows").GetArrayLength() == 500);
        chunks[^1].GetProperty("done").GetBoolean().Should().BeTrue();
        _Outcomes(chunks).Should().OnlyContain(static outcome => outcome == "done").And.HaveCount(5_000);
        _store.All().Should().OnlyContain(static run => run.State == FakeRunState.Queued && run.Requeues == 1);
        Module(app)
            .Chunks.Should()
            .OnlyContain(static request => request.ChunkSize == 500 && request.Reason == _Reason);
    }

    [Fact]
    public async Task should_skip_and_count_a_row_whose_state_changed_since_confirm()
    {
        _store.SeedFailed(600);
        await using var app = await _StartAsync();
        using var client = ShellTestHost.Client(app);
        var cookie = await ShellTestHost.SignInAsync(client, _Operator, AbortToken);
        var scope = await _ConfirmAsync(client, cookie);

        _store.SetState("run-000010", FakeRunState.Running);
        var first = await _ChunkAsync(client, cookie, scope, cursor: null);

        var row = first
            .GetProperty("rows")
            .EnumerateArray()
            .Single(static r => string.Equals(r.GetProperty("id").GetString(), "run-000010", StringComparison.Ordinal));
        row.GetProperty("outcome").GetString().Should().Be("skipped");
        var entry = _log.Entries.Single(static e => e.Action == OperatorLogAction.OperationChunk);
        entry.Done.Should().Be(499);
        entry.Skipped.Should().Be(1);
        _store.Get("run-000010").Requeues.Should().Be(0);
    }

    [Fact]
    public async Task should_not_touch_a_row_that_entered_the_failed_state_after_the_confirm_mark()
    {
        _store.SeedFailed(1_000);
        await using var app = await _StartAsync();
        using var client = ShellTestHost.Client(app);
        var cookie = await ShellTestHost.SignInAsync(client, _Operator, AbortToken);
        var scope = await _ConfirmAsync(client, cookie);

        // One run fails again during the Operation, and one new run fails: both entered the state after the mark.
        _store.SetState("run-000020", FakeRunState.Running);
        _store.SetState("run-000020", FakeRunState.Failed);
        _store.SeedFailed(1, first: 1_001);
        await _RunAllAsync(client, cookie, scope);

        _store
            .Get("run-000020")
            .Should()
            .Match<FakeRun>(static run => run.State == FakeRunState.Failed && run.Requeues == 0);
        _store
            .Get("run-001001")
            .Should()
            .Match<FakeRun>(static run => run.State == FakeRunState.Failed && run.Requeues == 0);
        _store.All().Count(static run => run.State == FakeRunState.Queued).Should().Be(999);
    }

    [Fact]
    public async Task should_count_only_the_remaining_rows_and_touch_no_row_twice_when_run_again_after_a_stop()
    {
        _store.SeedFailed(5_000);
        await using var app = await _StartAsync();
        using var client = ShellTestHost.Client(app);
        var cookie = await ShellTestHost.SignInAsync(client, _Operator, AbortToken);
        var first = await _ConfirmAsync(client, cookie);

        // Stop is the browser not asking for a fourth chunk.
        string? cursor = null;
        for (var i = 0; i < 3; i++)
        {
            cursor = (await _ChunkAsync(client, cookie, first, cursor)).GetProperty("nextCursor").GetString();
        }

        _store.All().Count(static run => run.State == FakeRunState.Queued).Should().Be(1_500);
        var second = await _ConfirmAsync(client, cookie);
        await _RunAllAsync(client, cookie, second);

        second.GetProperty("count").GetInt64().Should().Be(3_500);
        _store.All().Should().OnlyContain(static run => run.State == FakeRunState.Queued && run.Requeues == 1);
    }

    [Fact]
    public async Task should_produce_the_same_result_when_two_nodes_sharing_a_key_ring_and_store_serve_alternate_chunks()
    {
        _store.SeedFailed(5_000);
        var keyRing = new EphemeralDataProtectionProvider();
        await using var nodeA = await _StartAsync(keyRing);
        await using var nodeB = await _StartAsync(keyRing);
        using var clientA = ShellTestHost.Client(nodeA);
        using var clientB = ShellTestHost.Client(nodeB);
        var cookie = await ShellTestHost.SignInAsync(clientA, _Operator, AbortToken);
        var scope = await _ConfirmAsync(clientA, cookie);

        var chunks = new List<JsonElement>();
        string? cursor = null;
        var useA = true;
        do
        {
            var chunk = await _ChunkAsync(useA ? clientA : clientB, cookie, scope, cursor);
            chunks.Add(chunk);
            cursor = chunk.TryGetProperty("nextCursor", out var next) ? next.GetString() : null;
            useA = !useA;
        } while (cursor is not null);

        _Outcomes(chunks).Should().OnlyContain(static outcome => outcome == "done").And.HaveCount(5_000);
        _store.All().Should().OnlyContain(static run => run.State == FakeRunState.Queued && run.Requeues == 1);
        Module(nodeA).Chunks.Should().NotBeEmpty();
        Module(nodeB).Chunks.Should().NotBeEmpty();
    }

    [Fact]
    public async Task should_stop_the_next_chunk_with_403_when_the_operate_permission_is_revoked_between_chunks()
    {
        _store.SeedFailed(1_000);
        await using var app = await _StartAsync();
        using var client = ShellTestHost.Client(app);
        var cookie = await ShellTestHost.SignInAsync(client, _Operator, AbortToken);
        var scope = await _ConfirmAsync(client, cookie);
        var cursor = (await _ChunkAsync(client, cookie, scope, cursor: null)).GetProperty("nextCursor").GetString();

        _gate.Operate = false;
        using var response = await _PostChunkAsync(
            client,
            cookie,
            FakeCommandModule.Requeue,
            scope.GetProperty("token").GetString()!,
            cursor,
            scope.GetProperty("count").GetInt64()
        );

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _store.All().Count(static run => run.State == FakeRunState.Queued).Should().Be(500);
    }

    [Fact]
    public async Task should_stop_the_next_chunk_with_403_when_the_command_capability_is_revoked_between_chunks()
    {
        _store.SeedFailed(1_000);
        await using var app = await _StartAsync();
        using var client = ShellTestHost.Client(app);
        var cookie = await ShellTestHost.SignInAsync(client, _Operator, AbortToken);
        var scope = await _ConfirmAsync(client, cookie, FakeCommandModule.Purge);
        var cursor = (await _ChunkAsync(client, cookie, scope, cursor: null, FakeCommandModule.Purge))
            .GetProperty("nextCursor")
            .GetString();

        _gate.Purge = false;
        using var response = await _PostChunkAsync(
            client,
            cookie,
            FakeCommandModule.Purge,
            scope.GetProperty("token").GetString()!,
            cursor,
            scope.GetProperty("count").GetInt64()
        );

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Module(app).Chunks.Should().ContainSingle();
    }

    [Fact]
    public async Task should_refuse_with_400_a_token_confirmed_for_one_command_sent_to_another_command_chunks()
    {
        _store.SeedFailed(10);
        await using var app = await _StartAsync();
        using var client = ShellTestHost.Client(app);
        var cookie = await ShellTestHost.SignInAsync(client, _Operator, AbortToken);
        var requeue = await _ConfirmAsync(client, cookie);

        using var response = await _PostChunkAsync(
            client,
            cookie,
            FakeCommandModule.Purge,
            requeue.GetProperty("token").GetString()!,
            cursor: null,
            10
        );

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadAsync(response, AbortToken))
            .GetProperty("rules")
            .GetProperty("token")
            .GetString()
            .Should()
            .Be("scope");
        Module(app).Chunks.Should().BeEmpty();
    }

    [Fact]
    public async Task should_require_the_typed_count_on_each_chunk_when_the_scope_is_over_100_rows()
    {
        _store.SeedFailed(101);
        await using var app = await _StartAsync();
        using var client = ShellTestHost.Client(app);
        var cookie = await ShellTestHost.SignInAsync(client, _Operator, AbortToken);
        var scope = await _ConfirmAsync(client, cookie);

        using var clicked = await PostAsync(
            client,
            $"{Base}/{FakeCommandModule.Requeue}/chunks",
            new { token = scope.GetProperty("token").GetString(), confirmed = true },
            cookie,
            AbortToken
        );

        scope.GetProperty("confirmation").GetString().Should().Be("typed");
        clicked.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadAsync(clicked, AbortToken))
            .GetProperty("rules")
            .GetProperty("typedCount")
            .GetString()
            .Should()
            .Be("typed");
        Module(app).Chunks.Should().BeEmpty();
    }

    [Fact]
    public async Task should_log_one_confirm_entry_and_one_entry_per_chunk_with_every_field()
    {
        _store.SeedFailed(1_200);
        await using var app = await _StartAsync();
        using var client = ShellTestHost.Client(app);
        var cookie = await ShellTestHost.SignInAsync(client, _Operator, AbortToken);
        var scope = await _ConfirmAsync(client, cookie);

        await _RunAllAsync(client, cookie, scope);

        var operationId = scope.GetProperty("operationId").GetString();
        var entries = _log.Entries;
        entries
            .Select(static e => e.Action)
            .Should()
            .Equal(
                OperatorLogAction.OperationConfirmed,
                OperatorLogAction.OperationChunk,
                OperatorLogAction.OperationChunk,
                OperatorLogAction.OperationChunk
            );
        entries
            .Should()
            .OnlyContain(e =>
                e.Actor == "diag-operator"
                && e.Channel == DashboardChannels.Ui
                && e.Module == "diag"
                && e.CommandId == FakeCommandModule.Requeue
                && e.Reason == _Reason
                && e.OperationId == operationId
                && e.Scope == """{"queue":"default"}"""
            );
        entries[0].Count.Should().Be(1_200);
        entries.Skip(1).Select(static e => (e.Count, e.Done)).Should().Equal((500, 500), (500, 500), (200, 200));
        entries[0].Cursor.Should().BeNull();
        entries[1].Cursor.Should().BeNull();
        entries.Skip(2).Should().OnlyContain(static e => !string.IsNullOrEmpty(e.Cursor));
        entries[1].Ids.Should().HaveCount(500).And.StartWith("run-000001");
    }

    private Task<WebApplication> _StartAsync(IDataProtectionProvider? keyRing = null)
    {
        return CommandTestHost.StartAsync(
            new CommandTestHostOptions
            {
                Store = _store,
                Time = _time,
                Gate = _gate,
                Log = _log,
                DataProtection = keyRing,
            },
            AbortToken
        );
    }

    private Task<HttpResponseMessage> _PostConfirmAsync(HttpClient client, string cookie, string command)
    {
        return PostAsync(
            client,
            $"{Base}/{command}/confirm",
            new { filter = new { queue = "default" }, reason = _Reason },
            cookie,
            AbortToken
        );
    }

    private async Task<JsonElement> _ConfirmAsync(
        HttpClient client,
        string cookie,
        string command = FakeCommandModule.Requeue
    )
    {
        using var response = await _PostConfirmAsync(client, cookie, command);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return await ReadAsync(response, AbortToken);
    }

    private Task<HttpResponseMessage> _PostChunkAsync(
        HttpClient client,
        string cookie,
        string command,
        string token,
        string? cursor,
        long typedCount
    )
    {
        return PostAsync(
            client,
            $"{Base}/{command}/chunks",
            new
            {
                token,
                cursor,
                confirmed = true,
                typedCount,
            },
            cookie,
            AbortToken
        );
    }

    private async Task<JsonElement> _ChunkAsync(
        HttpClient client,
        string cookie,
        JsonElement scope,
        string? cursor,
        string command = FakeCommandModule.Requeue
    )
    {
        using var response = await _PostChunkAsync(
            client,
            cookie,
            command,
            scope.GetProperty("token").GetString()!,
            cursor,
            scope.GetProperty("count").GetInt64()
        );
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return await ReadAsync(response, AbortToken);
    }

    private async Task<List<JsonElement>> _RunAllAsync(HttpClient client, string cookie, JsonElement scope)
    {
        var chunks = new List<JsonElement>();
        string? cursor = null;

        do
        {
            var chunk = await _ChunkAsync(client, cookie, scope, cursor);
            chunks.Add(chunk);
            cursor = chunk.TryGetProperty("nextCursor", out var next) ? next.GetString() : null;
        } while (cursor is not null);

        return chunks;
    }

    private static string?[] _Outcomes(IEnumerable<JsonElement> chunks)
    {
        return
        [
            .. chunks.SelectMany(static chunk =>
                chunk.GetProperty("rows").EnumerateArray().Select(static row => row.GetProperty("outcome").GetString())
            ),
        ];
    }
}
