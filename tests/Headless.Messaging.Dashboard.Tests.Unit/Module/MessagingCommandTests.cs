// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Security.Claims;
using Headless.Dashboard;
using Headless.Messaging;
using Headless.Messaging.Dashboard;
using Headless.Messaging.Persistence;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Tests.Module;

/// <summary>The Messaging commands and Operations as an operator reaches them: through the shell's command routes.</summary>
public sealed class MessagingCommandTests : TestBase
{
    private const string _Commands = MessagingCommandHost.Commands;

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero));
    private readonly RecordingOperatorLog _log = new();

    [Fact]
    public async Task should_disable_every_command_and_refuse_a_call_when_the_user_may_only_view()
    {
        // given
        await using var app = await MessagingCommandHost.StartAsync(_time, _log, AbortToken);
        var storage = app.Services.GetRequiredService<IDataStorage>();
        var row = await MessagingCommandHost.InboxFailureAsync(storage, "order-1", AbortToken);
        var id = _ReceivedId(row);
        using var client = MessagingCommandHost.Client(app);
        var cookie = await MessagingCommandHost.SignInAsync(client, "viewer", AbortToken);

        // when
        using var allowed = await MessagingCommandHost.GetAsync(
            client,
            $"{_Commands}/allowed?kind=received&id={Uri.EscapeDataString(id)}",
            cookie,
            AbortToken
        );
        using var run = await MessagingCommandHost.PostAsync(
            client,
            $"{_Commands}/{MessagingCommands.ReExecuteId}",
            new { ids = new[] { id }, confirmed = true },
            cookie,
            AbortToken
        );

        // then
        allowed.StatusCode.Should().Be(HttpStatusCode.OK);
        var commands = (await MessagingCommandHost.ReadAsync(allowed, AbortToken))
            .GetProperty("objects")[0]
            .GetProperty("commands")
            .EnumerateArray()
            .ToArray();
        commands.Should().HaveCount(4);
        commands.Should().OnlyContain(command => !command.GetProperty("allowed").GetBoolean());
        commands
            .Select(command => command.GetProperty("disabledReason").GetString())
            .Should()
            .AllBe("Needs operate permission");
        run.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _log.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task should_hold_without_a_reason_and_refuse_re_execute_until_released_when_the_row_is_held()
    {
        // given
        await using var app = await MessagingCommandHost.StartAsync(_time, _log, AbortToken);
        var storage = app.Services.GetRequiredService<IDataStorage>();
        var row = await MessagingCommandHost.InboxFailureAsync(storage, "order-1", AbortToken);
        var id = _ReceivedId(row);
        using var client = MessagingCommandHost.Client(app);
        var cookie = await MessagingCommandHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var hold = await MessagingCommandHost.PostAsync(
            client,
            $"{_Commands}/{MessagingCommands.HoldId}",
            new { ids = new[] { id } },
            cookie,
            AbortToken
        );
        using var allowed = await MessagingCommandHost.GetAsync(
            client,
            $"{_Commands}/allowed?kind=received&id={Uri.EscapeDataString(id)}",
            cookie,
            AbortToken
        );
        using var reExecute = await MessagingCommandHost.PostAsync(
            client,
            $"{_Commands}/{MessagingCommands.ReExecuteId}",
            new { ids = new[] { id }, confirmed = true },
            cookie,
            AbortToken
        );

        // then
        hold.StatusCode.Should().Be(HttpStatusCode.OK);
        var held = (await MessagingCommandHost.ReadAsync(hold, AbortToken)).GetProperty("rows")[0];
        held.GetProperty("id").GetString().Should().Be(id);
        held.GetProperty("outcome").GetString().Should().Be("done");

        var reExecuteAnswer = _Command(
            await MessagingCommandHost.ReadAsync(allowed, AbortToken),
            MessagingCommands.ReExecuteId
        );
        reExecuteAnswer.GetProperty("allowed").GetBoolean().Should().BeFalse();
        reExecuteAnswer.GetProperty("disabledReason").GetString().Should().Be(MessagingCommands.ReleaseFirst);

        reExecute.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await MessagingCommandHost.ReadAsync(reExecute, AbortToken))
            .GetProperty("detail")
            .GetString()
            .Should()
            .Be(MessagingCommands.ReleaseFirst);

        // The hold ran and is logged; the refused re-execute changed nothing and is not.
        _log.Entries.Should()
            .ContainSingle()
            .Which.Should()
            .Match<OperatorLogEntry>(entry =>
                entry.Action == OperatorLogAction.Run
                && entry.CommandId == MessagingCommands.HoldId
                && entry.Actor == "ops"
                && entry.Done == 1
            );
    }

    [Fact]
    public async Task should_reject_an_operation_confirm_when_it_names_no_reason()
    {
        // given
        await using var app = await MessagingCommandHost.StartAsync(_time, _log, AbortToken);
        var storage = app.Services.GetRequiredService<IDataStorage>();
        await MessagingCommandHost.InboxFailureAsync(storage, "order-1", AbortToken);
        using var client = MessagingCommandHost.Client(app);
        var cookie = await MessagingCommandHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var confirm = await MessagingCommandHost.PostAsync(
            client,
            $"{_Commands}/{MessagingCommands.ReExecuteId}/confirm",
            new { filter = new { status = "Failed" } },
            cookie,
            AbortToken
        );

        // then
        confirm.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await MessagingCommandHost.ReadAsync(confirm, AbortToken))
            .GetProperty("errors")
            .TryGetProperty("reason", out _)
            .Should()
            .BeTrue();
        _log.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task should_refuse_dispatch_now_and_allow_revoke_when_the_scheduled_row_is_leased()
    {
        // given
        await using var app = await MessagingCommandHost.StartAsync(_time, _log, AbortToken);
        var storage = app.Services.GetRequiredService<IDataStorage>();
        var scheduled = await storage.StoreScheduledMessageAsync(
            MessagingRows.Name,
            MessagingRows.Medium("invoice-1"),
            _time.GetUtcNow().AddHours(1),
            cancellationToken: AbortToken
        );
        (await storage.LeasePublishAsync(scheduled, TimeSpan.FromMinutes(5), AbortToken)).Should().BeTrue();
        var id = $"node-a~primary~{scheduled.StorageId:D}";
        using var client = MessagingCommandHost.Client(app);
        var cookie = await MessagingCommandHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var dispatch = await MessagingCommandHost.PostAsync(
            client,
            $"{_Commands}/{MessagingCommands.DispatchNowId}",
            new { ids = new[] { id } },
            cookie,
            AbortToken
        );
        using var revoke = await MessagingCommandHost.PostAsync(
            client,
            $"{_Commands}/{MessagingCommands.RevokeId}",
            new { ids = new[] { id }, confirmed = true },
            cookie,
            AbortToken
        );

        // then
        dispatch.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await MessagingCommandHost.ReadAsync(dispatch, AbortToken))
            .GetProperty("detail")
            .GetString()
            .Should()
            .Be(MessagingCommands.Leased);
        revoke.StatusCode.Should().Be(HttpStatusCode.OK);
        (await MessagingCommandHost.ReadAsync(revoke, AbortToken))
            .GetProperty("rows")[0]
            .GetProperty("outcome")
            .GetString()
            .Should()
            .Be("done");
    }

    [Fact]
    public async Task should_re_execute_each_row_once_when_an_operation_is_stopped_and_resumed_on_another_node()
    {
        // given
        const int rowCount = 5_000;
        await using var nodeA = await MessagingCommandHost.StartAsync(_time, _log, AbortToken, nodeName: "node-a");
        var storage = nodeA.Services.GetRequiredService<IDataStorage>();
        var protection = nodeA.Services.GetRequiredService<IDataProtectionProvider>();
        await using var nodeB = await MessagingCommandHost.StartAsync(
            _time,
            _log,
            AbortToken,
            nodeName: "node-b",
            sharedStorage: storage,
            sharedProtection: protection
        );

        for (var i = 0; i < rowCount; i++)
        {
            await MessagingCommandHost.InboxFailureAsync(
                storage,
                "order-" + i.ToString(CultureInfo.InvariantCulture),
                AbortToken
            );
        }

        using var clientA = MessagingCommandHost.Client(nodeA);
        using var clientB = MessagingCommandHost.Client(nodeB);
        var cookieA = await MessagingCommandHost.SignInAsync(clientA, "ops", AbortToken);
        var cookieB = await MessagingCommandHost.SignInAsync(clientB, "ops", AbortToken);

        using var confirm = await MessagingCommandHost.PostAsync(
            clientA,
            $"{_Commands}/{MessagingCommands.ReExecuteId}/confirm",
            new { filter = new { status = "Failed" }, reason = "card processor recovered" },
            cookieA,
            AbortToken
        );
        confirm.StatusCode.Should().Be(HttpStatusCode.OK);
        var scope = await MessagingCommandHost.ReadAsync(confirm, AbortToken);
        scope.GetProperty("count").GetInt64().Should().Be(rowCount);
        var token = scope.GetProperty("token").GetString()!;
        var done = new List<string>();

        // when: node A runs two chunks and stops; node B resumes from the start of the scope
        string? cursor = null;

        for (var chunk = 0; chunk < 2; chunk++)
        {
            var answer = await _ChunkAsync(clientA, cookieA, token, cursor, rowCount);
            done.AddRange(_Done(answer));
            cursor = answer.GetProperty("nextCursor").GetString();
            cursor.Should().NotBeNull();
        }

        var (resumedDone, _) = await _RunToEndAsync(clientB, cookieB, token, rowCount);
        var (_, rerunRows) = await _RunToEndAsync(clientB, cookieB, token, rowCount);

        // then
        done.Should().HaveCount(1_000);
        resumedDone.Should().HaveCount(rowCount - 1_000);
        done.Concat(resumedDone).Select(_StorageIdOf).Should().OnlyHaveUniqueItems().And.HaveCount(rowCount);
        rerunRows.Should().Be(0);

        // Every re-execute creates exactly one new generation, so the store holds each row and one child of it.
        var count = await storage
            .GetMonitoringApi()
            .CountMessagesAsync(new MessageListQuery { MessageType = MessageType.Subscribe }, 3 * rowCount, AbortToken);
        count.Count.Should().Be(2 * rowCount);
        _log.Entries.Should().Contain(entry => entry.Action == OperatorLogAction.OperationConfirmed);
        _log.Entries.Where(entry => entry.Action == OperatorLogAction.OperationChunk)
            .Sum(entry => entry.Done)
            .Should()
            .Be(rowCount);
    }

    [Fact]
    public async Task should_record_actor_and_reason_on_the_receipt_when_a_failed_published_row_is_re_sent()
    {
        // given
        await using var app = await MessagingCommandHost.StartAsync(_time, _log, AbortToken);
        var storage = app.Services.GetRequiredService<IDataStorage>();
        var row = await MessagingRows.PublishedAsync(storage, "invoice-1", StatusName.Failed, AbortToken);
        var id = $"node-a~primary~{row.StorageId:D}";
        using var client = MessagingCommandHost.Client(app);
        var cookie = await MessagingCommandHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var resend = await MessagingCommandHost.PostAsync(
            client,
            $"{_Commands}/{MessagingCommands.ResendId}",
            new
            {
                ids = new[] { id },
                reason = "broker credentials rotated",
                confirmed = true,
            },
            cookie,
            AbortToken
        );

        // then
        resend.StatusCode.Should().Be(HttpStatusCode.OK);
        (await MessagingCommandHost.ReadAsync(resend, AbortToken))
            .GetProperty("rows")[0]
            .GetProperty("outcome")
            .GetString()
            .Should()
            .Be("done");
        var receipts = await storage
            .GetInboxOperationsApi()
            .QueryReceiptsAsync(
                new MessagingOperationReceiptQuery { TargetKind = MessagingOperationTargetKind.Published },
                new OperatorAuthorizationContext(_Operator()),
                AbortToken
            );
        var receipt = receipts.Items.Should().ContainSingle().Subject;
        receipt.OperationType.Should().Be(MessagingOperationType.Resend);
        receipt.Actor.Should().Be("ops");
        receipt.Reason.Should().Be("broker credentials rotated");
        receipt.StorageId.Should().Be(row.StorageId);
    }

    [Fact]
    public async Task should_reset_the_circuit_on_this_node_and_log_it_when_the_circuit_is_open()
    {
        // given
        var monitor = Substitute.For<ICircuitBreakerMonitor>();
        monitor.GetSnapshot(MessagingRows.Consumer).Returns(MessagingCommandHost.Snapshot(CircuitBreakerState.Open));
        monitor.ResetAsync(MessagingRows.Consumer, Arg.Any<CancellationToken>()).Returns(true);
        await using var app = await MessagingCommandHost.StartAsync(
            _time,
            _log,
            AbortToken,
            configure: services => services.AddSingleton(monitor)
        );
        var id = MessagingViews.CircuitId("node-a", MessagingRows.Consumer);
        using var client = MessagingCommandHost.Client(app);
        var cookie = await MessagingCommandHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var reset = await MessagingCommandHost.PostAsync(
            client,
            $"{_Commands}/{MessagingCommands.ResetCircuitId}",
            new { ids = new[] { id } },
            cookie,
            AbortToken
        );

        // then
        reset.StatusCode.Should().Be(HttpStatusCode.OK);
        (await MessagingCommandHost.ReadAsync(reset, AbortToken))
            .GetProperty("rows")[0]
            .GetProperty("outcome")
            .GetString()
            .Should()
            .Be("done");
        await monitor.Received(1).ResetAsync(MessagingRows.Consumer, Arg.Any<CancellationToken>());
        _log.Entries.Should()
            .ContainSingle()
            .Which.Should()
            .Match<OperatorLogEntry>(entry =>
                entry.Action == OperatorLogAction.Run
                && entry.CommandId == MessagingCommands.ResetCircuitId
                && entry.Done == 1
            );
    }

    private static string _ReceivedId(MediumMessage row) => $"node-a~primary~{row.StorageId:D}";

    private static string _StorageIdOf(string id) => id[(id.LastIndexOf('~') + 1)..];

    private static JsonElement _Command(JsonElement allowed, string commandId) =>
        allowed
            .GetProperty("objects")[0]
            .GetProperty("commands")
            .EnumerateArray()
            .Single(command =>
                string.Equals(command.GetProperty("id").GetString(), commandId, StringComparison.Ordinal)
            );

    private static IEnumerable<string> _Done(JsonElement chunk) =>
        chunk
            .GetProperty("rows")
            .EnumerateArray()
            .Where(row => string.Equals(row.GetProperty("outcome").GetString(), "done", StringComparison.Ordinal))
            .Select(row => row.GetProperty("id").GetString()!);

    private async Task<JsonElement> _ChunkAsync(
        HttpClient client,
        string cookie,
        string token,
        string? cursor,
        int count
    )
    {
        using var response = await MessagingCommandHost.PostAsync(
            client,
            $"{_Commands}/{MessagingCommands.ReExecuteId}/chunks",
            new
            {
                token,
                cursor,
                confirmed = true,
                typedCount = count,
            },
            cookie,
            AbortToken
        );
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return await MessagingCommandHost.ReadAsync(response, AbortToken);
    }

    private async Task<(List<string> Done, int Rows)> _RunToEndAsync(
        HttpClient client,
        string cookie,
        string token,
        int count
    )
    {
        var done = new List<string>();
        var rows = 0;
        string? cursor = null;

        do
        {
            var answer = await _ChunkAsync(client, cookie, token, cursor, count);
            rows += answer.GetProperty("rows").GetArrayLength();
            done.AddRange(_Done(answer));
            cursor = answer.TryGetProperty("nextCursor", out var next) ? next.GetString() : null;
        } while (cursor is not null);

        return (done, rows);
    }

    private static ClaimsPrincipal _Operator() => new(new ClaimsIdentity([new Claim(ClaimTypes.Name, "ops")], "test"));
}
