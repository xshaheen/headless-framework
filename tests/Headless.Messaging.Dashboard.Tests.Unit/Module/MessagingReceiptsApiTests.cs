// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Security.Claims;
using Headless.Messaging;
using Headless.Messaging.Persistence;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Tests.Forwarding;

namespace Tests.Module;

/// <summary>
/// The operator ledger at <c>{base}/api/messaging/receipts</c>: its filters, its cursor paging, who may read it, and
/// a read relayed to another node.
/// </summary>
public sealed class MessagingReceiptsApiTests : TestBase
{
    private const string _Receipts = MessagingModuleTestHost.Api + "/receipts";

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task should_return_only_the_receipts_that_match_every_filter_when_actor_and_operation_type_are_set()
    {
        // given: alice holds and releases one row, bob holds another
        await using var app = await MessagingModuleTestHost.StartAsync(_time, AbortToken);
        var storage = app.Services.GetRequiredService<IDataStorage>();
        var first = await MessagingCommandHost.InboxFailureAsync(storage, "order-1", AbortToken);
        var second = await MessagingCommandHost.InboxFailureAsync(storage, "order-2", AbortToken);
        var aliceHold = await _HoldAsync(storage, first, "alice");
        await _ReleaseAsync(storage, first, "alice");
        await _HoldAsync(storage, second, "bob");
        using var client = app.GetTestClient();

        // when
        var body = await MessagingModuleTestHost.GetJsonAsync(
            client,
            $"{_Receipts}?actor=alice&operationType=hold&targetKind=inbox",
            AbortToken
        );

        // then
        var receipt = body.GetProperty("items").EnumerateArray().Should().ContainSingle().Subject;
        receipt.GetProperty("operationId").GetGuid().Should().Be(aliceHold);
        receipt.GetProperty("actor").GetString().Should().Be("alice");
        receipt.GetProperty("operationType").GetString().Should().Be("Hold");
        receipt.GetProperty("targetKind").GetString().Should().Be("Inbox");
        receipt.GetProperty("outcome").GetString().Should().Be("Applied");
        receipt.GetProperty("storageId").GetGuid().Should().Be(first.StorageId);
        body.TryGetProperty("nextCursor", out _).Should().BeFalse();
    }

    [Fact]
    public async Task should_page_newest_first_without_repeating_a_receipt_when_the_next_cursor_is_sent_back()
    {
        // given: three receipts, a second apart
        await using var app = await MessagingModuleTestHost.StartAsync(_time, AbortToken);
        var storage = app.Services.GetRequiredService<IDataStorage>();
        var row = await MessagingCommandHost.InboxFailureAsync(storage, "order-1", AbortToken);
        var oldest = await _HoldAsync(storage, row, "alice");
        _time.Advance(TimeSpan.FromSeconds(1));
        var middle = await _ReleaseAsync(storage, row, "alice");
        _time.Advance(TimeSpan.FromSeconds(1));
        var newest = await _HoldAsync(storage, row, "alice");
        using var client = app.GetTestClient();

        // when
        var firstPage = await MessagingModuleTestHost.GetJsonAsync(client, $"{_Receipts}?limit=2", AbortToken);
        var cursor = firstPage.GetProperty("nextCursor").GetString();
        var secondPage = await MessagingModuleTestHost.GetJsonAsync(
            client,
            $"{_Receipts}?limit=2&after={Uri.EscapeDataString(cursor!)}",
            AbortToken
        );

        // then
        _OperationIds(firstPage).Should().Equal(newest, middle);
        _OperationIds(secondPage).Should().Equal(oldest);
        secondPage.TryGetProperty("nextCursor", out _).Should().BeFalse();
    }

    [Fact]
    public async Task should_answer_400_when_the_cursor_or_a_filter_is_not_one_the_ledger_knows()
    {
        // given
        await using var app = await MessagingModuleTestHost.StartAsync(_time, AbortToken);
        using var client = app.GetTestClient();

        // when
        using var cursor = await client.GetAsync(
            new Uri($"{_Receipts}?after=s1|primary=-", UriKind.Relative),
            AbortToken
        );
        using var kind = await client.GetAsync(new Uri($"{_Receipts}?targetKind=queue", UriKind.Relative), AbortToken);
        using var type = await client.GetAsync(new Uri($"{_Receipts}?operationType=42", UriKind.Relative), AbortToken);

        // then
        cursor.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        kind.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        type.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task should_let_a_viewer_read_the_ledger_and_refuse_a_caller_who_is_not_signed_in()
    {
        // given
        await using var app = await MessagingCommandHost.StartAsync(_time, new RecordingOperatorLog(), AbortToken);
        var storage = app.Services.GetRequiredService<IDataStorage>();
        var row = await MessagingCommandHost.InboxFailureAsync(storage, "order-1", AbortToken);
        var hold = await _HoldAsync(storage, row, "ops");
        using var client = MessagingCommandHost.Client(app);
        var cookie = await MessagingCommandHost.SignInAsync(client, "viewer", AbortToken);

        // when
        using var viewer = await MessagingCommandHost.GetAsync(client, _Receipts, cookie, AbortToken);
        using var anonymous = await client.GetAsync(new Uri(_Receipts, UriKind.Relative), AbortToken);

        // then
        viewer.StatusCode.Should().Be(HttpStatusCode.OK);
        _OperationIds(await MessagingCommandHost.ReadAsync(viewer, AbortToken)).Should().Equal(hold);
        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task should_relay_the_read_to_node_b_and_return_its_ledger_when_the_node_parameter_names_node_b()
    {
        // given
        var network = new PeerNetwork();
        var discoveryA = new StaticDiscovery();
        discoveryA.Set(ForwardingHost.Entry("node-b", "https://node-b"));
        await using var nodeB = await ForwardingHost.StartAsync(
            "node-b",
            network,
            new StaticDiscovery(),
            _time,
            new RecordingOperatorLog(),
            AbortToken
        );
        network.Add("node-b", nodeB);
        await using var nodeA = await ForwardingHost.StartAsync(
            "node-a",
            network,
            discoveryA,
            _time,
            new RecordingOperatorLog(),
            AbortToken
        );
        var storageB = nodeB.Services.GetRequiredService<IDataStorage>();
        var row = await MessagingCommandHost.InboxFailureAsync(storageB, "order-1", AbortToken);
        var hold = await _HoldAsync(storageB, row, "ops");
        using var client = MessagingCommandHost.Client(nodeA);
        var cookie = await MessagingCommandHost.SignInAsync(client, "viewer", AbortToken);

        // when
        using var response = await MessagingCommandHost.GetAsync(
            client,
            $"{_Receipts}?node=node-b",
            cookie,
            AbortToken
        );

        // then
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _OperationIds(await MessagingCommandHost.ReadAsync(response, AbortToken)).Should().Equal(hold);
        var call = network.Calls.Should().ContainSingle().Subject;
        call.Uri.Should().Be(new Uri("https://node-b/headless/api/messaging/receipts"));
    }

    private static Task<Guid> _HoldAsync(IDataStorage storage, MediumMessage row, string actor) =>
        _ApplyAsync(storage, row, actor, static (inbox, request) => inbox.HoldAsync(request));

    private static Task<Guid> _ReleaseAsync(IDataStorage storage, MediumMessage row, string actor) =>
        _ApplyAsync(storage, row, actor, static (inbox, request) => inbox.ReleaseHoldAsync(request));

    // Each applied operation writes one receipt under its operation identifier.
    private static async Task<Guid> _ApplyAsync(
        IDataStorage storage,
        MediumMessage row,
        string actor,
        Func<IInboxOperationsApi, InboxOperationRequest, ValueTask<InboxOperationResult>> operation
    )
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, actor)], "Test"));
        var request = new InboxOperationRequest(
            Guid.CreateVersion7(),
            row.InboxGeneration!.IncarnationId,
            StatusName.Failed,
            Reason: null,
            new OperatorAuthorizationContext(principal)
        );
        var result = await operation(storage.GetInboxOperationsApi(), request);
        result.Outcome.Should().Be(InboxOperationOutcome.Applied);

        return result.OperationId;
    }

    private static Guid[] _OperationIds(JsonElement body) =>
        [
            .. body.GetProperty("items")
                .EnumerateArray()
                .Select(static item => item.GetProperty("operationId").GetGuid()),
        ];
}
