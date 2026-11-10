// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Dashboard;
using Headless.Messaging;
using Headless.Messaging.Dashboard;
using Headless.Messaging.Persistence;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Tests.Module;

namespace Tests.Forwarding;

/// <summary>
/// A command on another node's row, sent from node A to node B: who B admits, at what access, and what B records.
/// </summary>
public sealed class ForwardingCommandTests : TestBase
{
    private const string _Commands = MessagingCommandHost.Commands;

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero));
    private readonly PeerNetwork _network = new();
    private readonly StaticDiscovery _discoveryA = new();
    private readonly StaticDiscovery _discoveryB = new();
    private readonly RecordingOperatorLog _logA = new();
    private readonly RecordingOperatorLog _logB = new();

    public ForwardingCommandTests()
    {
        _discoveryA.Set(ForwardingHost.Entry("node-b", "https://node-b"));
    }

    [Fact]
    public async Task should_run_on_node_b_and_record_the_operator_via_node_a_when_node_a_forwards_a_re_execute()
    {
        // given
        await using var nodeB = await _StartBAsync(_Operate);
        await using var nodeA = await _StartAAsync();
        var storageB = nodeB.Services.GetRequiredService<IDataStorage>();
        var row = await MessagingCommandHost.InboxFailureAsync(storageB, "order-1", AbortToken);
        using var client = MessagingCommandHost.Client(nodeA);
        var cookie = await MessagingCommandHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var response = await _ReExecuteAsync(client, cookie, _IdOnB(row));

        // then
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await MessagingCommandHost.ReadAsync(response, AbortToken)).GetProperty("rows")[0];
        result.GetProperty("id").GetString().Should().Be(_IdOnB(row));
        result.GetProperty("outcome").GetString().Should().Be("done");

        _logB
            .Entries.Should()
            .ContainSingle()
            .Which.Should()
            .Match<OperatorLogEntry>(entry =>
                entry.Action == OperatorLogAction.Run
                && entry.CommandId == MessagingCommands.ReExecuteId
                && entry.Actor == "ops via node-a"
                && entry.Done == 1
            );
        (await _ReceivedCountAsync(storageB)).Should().Be(2);

        var call = _network.Calls.Should().ContainSingle(sent => sent.Method == HttpMethod.Post).Subject;
        call.Caller.Should().Be("node-a");
        call.Uri.Should().Be(new Uri($"https://node-b{_Commands}/{MessagingCommands.ReExecuteId}"));
        call.CarriedKey.Should().BeTrue();
        call.Access.Should().Be(ForwardingProtocol.AccessOperate);
        call.Actor.Should().Be("ops");
    }

    [Fact]
    public async Task should_refuse_with_401_and_touch_no_row_when_the_forwarding_key_is_wrong_or_missing()
    {
        // given
        await using var nodeB = await _StartBAsync(options =>
        {
            options.Forwarding.Key = new string('x', MessagingForwardingOptions.MinimumKeyLength);
            options.ForwardedAccess = MessagingForwardedAccess.Operate;
        });
        await using var nodeA = await _StartAAsync();
        var storageB = nodeB.Services.GetRequiredService<IDataStorage>();
        var row = await MessagingCommandHost.InboxFailureAsync(storageB, "order-1", AbortToken);
        using var client = MessagingCommandHost.Client(nodeA);
        using var clientB = MessagingCommandHost.Client(nodeB);
        var cookie = await MessagingCommandHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var forwarded = await _ReExecuteAsync(client, cookie, _IdOnB(row));
        using var wrongKey = await _SendAsPeerAsync(clientB, _ReExecuteBody(_IdOnB(row)), ForwardingHost.Key);
        using var noKey = await _SendAsPeerAsync(clientB, _ReExecuteBody(_IdOnB(row)), key: null);

        // then
        forwarded.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await _DetailAsync(forwarded)).Should().Be("Node node-b rejected this node's forwarding key");
        nodeA.Services.GetRequiredService<PeerDirectory>().StatusOf("node-b")!.State.Should().Be(PeerState.KeyRejected);

        foreach (var refused in new[] { wrongKey, noKey })
        {
            refused.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            _Refusal(refused).Should().Be(ForwardingProtocol.RefusalKey);
            (await refused.Content.ReadAsStringAsync(AbortToken)).Should().NotContain(ForwardingHost.Key);
        }

        _logB.Entries.Should().BeEmpty();
        (await _ReceivedCountAsync(storageB)).Should().Be(1);
    }

    [Fact]
    public async Task should_refuse_with_403_policy_denied_and_touch_no_row_when_the_host_policy_denies_the_node()
    {
        // given
        await using var nodeB = await _StartBAsync(_PolicyMode, PeerIdentityHandler.AddTo);
        await using var nodeA = await _StartAAsync(_PolicyMode, services => _PresentIdentity(services, "rogue"));
        var storageB = nodeB.Services.GetRequiredService<IDataStorage>();
        var row = await MessagingCommandHost.InboxFailureAsync(storageB, "order-1", AbortToken);
        using var client = MessagingCommandHost.Client(nodeA);
        var cookie = await MessagingCommandHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var forwarded = await _ReExecuteAsync(client, cookie, _IdOnB(row));

        // then
        forwarded.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await _DetailAsync(forwarded)).Should().Be("Node node-b denied this node by its forwarding policy");
        nodeA
            .Services.GetRequiredService<PeerDirectory>()
            .StatusOf("node-b")!
            .State.Should()
            .Be(PeerState.PolicyDenied);
        _logB.Entries.Should().BeEmpty();
        (await _ReceivedCountAsync(storageB)).Should().Be(1);
    }

    [Fact]
    public async Task should_admit_the_node_by_the_host_policy_and_send_no_key_when_a_policy_replaces_the_key()
    {
        // given
        await using var nodeB = await _StartBAsync(_PolicyMode, PeerIdentityHandler.AddTo);
        await using var nodeA = await _StartAAsync(_PolicyMode, services => _PresentIdentity(services, "node-a"));
        var storageB = nodeB.Services.GetRequiredService<IDataStorage>();
        var row = await MessagingCommandHost.InboxFailureAsync(storageB, "order-1", AbortToken);
        using var client = MessagingCommandHost.Client(nodeA);
        var cookie = await MessagingCommandHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var forwarded = await _ReExecuteAsync(client, cookie, _IdOnB(row));

        // then
        forwarded.StatusCode.Should().Be(HttpStatusCode.OK);
        _logB.Entries.Should().ContainSingle().Which.Actor.Should().Be("ops via node-a");
        _network.Calls.Should().NotBeEmpty().And.OnlyContain(sent => !sent.CarriedKey);
    }

    [Fact]
    public async Task should_refuse_with_403_access_denied_when_a_forwarded_write_asserts_view()
    {
        // given
        await using var nodeB = await _StartBAsync(_Operate);
        var storageB = nodeB.Services.GetRequiredService<IDataStorage>();
        var row = await MessagingCommandHost.InboxFailureAsync(storageB, "order-1", AbortToken);
        using var clientB = MessagingCommandHost.Client(nodeB);

        // when
        using var response = await _SendAsPeerAsync(
            clientB,
            _ReExecuteBody(_IdOnB(row)),
            ForwardingHost.Key,
            access: ForwardingProtocol.AccessView
        );

        // then
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _Refusal(response).Should().Be(ForwardingProtocol.RefusalAccess);
        _logB.Entries.Should().BeEmpty();
        (await _ReceivedCountAsync(storageB)).Should().Be(1);
    }

    [Fact]
    public async Task should_name_the_view_only_ceiling_when_node_b_caps_forwarded_calls_at_view()
    {
        // given: node B keeps the default ceiling
        await using var nodeB = await _StartBAsync();
        await using var nodeA = await _StartAAsync();
        var storageB = nodeB.Services.GetRequiredService<IDataStorage>();
        var row = await MessagingCommandHost.InboxFailureAsync(storageB, "order-1", AbortToken);
        var id = _IdOnB(row);
        using var client = MessagingCommandHost.Client(nodeA);
        var cookie = await MessagingCommandHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var allowed = await MessagingCommandHost.GetAsync(
            client,
            $"{_Commands}/allowed?kind=received&id={Uri.EscapeDataString(id)}",
            cookie,
            AbortToken
        );
        using var run = await _ReExecuteAsync(client, cookie, id);

        // then
        allowed.StatusCode.Should().Be(HttpStatusCode.OK);
        var reExecute = (await MessagingCommandHost.ReadAsync(allowed, AbortToken))
            .GetProperty("objects")[0]
            .GetProperty("commands")
            .EnumerateArray()
            .Single(command =>
                string.Equals(
                    command.GetProperty("id").GetString(),
                    MessagingCommands.ReExecuteId,
                    StringComparison.Ordinal
                )
            );
        reExecute.GetProperty("allowed").GetBoolean().Should().BeFalse();
        reExecute
            .GetProperty("disabledReason")
            .GetString()
            .Should()
            .Be("Node node-b accepts view-only forwarded calls");

        run.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await _DetailAsync(run)).Should().Be("Node node-b accepts view-only forwarded calls");
        _logB.Entries.Should().BeEmpty();
        (await _ReceivedCountAsync(storageB)).Should().Be(1);
    }

    [Fact]
    public async Task should_judge_a_signed_in_viewer_by_the_session_when_it_forges_the_forwarding_headers()
    {
        // given
        await using var nodeB = await _StartBAsync(_Operate);
        var storageB = nodeB.Services.GetRequiredService<IDataStorage>();
        var row = await MessagingCommandHost.InboxFailureAsync(storageB, "order-1", AbortToken);
        using var clientB = MessagingCommandHost.Client(nodeB);
        var viewer = await MessagingCommandHost.SignInAsync(clientB, "viewer", AbortToken);

        // when: the viewer's browser sends the hop, operator, and operate headers without the key
        using var write = await _SendAsPeerAsync(clientB, _ReExecuteBody(_IdOnB(row)), key: null, cookie: viewer);
        using var peerSurface = await MessagingCommandHost.GetAsync(
            clientB,
            "/headless/api/messaging/peer/attention",
            viewer,
            AbortToken
        );

        // then
        write.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        peerSurface.StatusCode.Should().Be(HttpStatusCode.NotFound);
        _logB.Entries.Should().BeEmpty();
        (await _ReceivedCountAsync(storageB)).Should().Be(1);
    }

    [Fact]
    public async Task should_send_nothing_and_name_an_insecure_peer_when_discovery_lists_an_http_address()
    {
        // given
        _discoveryA.Set(ForwardingHost.Entry("node-b", "http://node-b"));
        await using var nodeB = await _StartBAsync(_Operate);
        await using var nodeA = await _StartAAsync();
        var storageB = nodeB.Services.GetRequiredService<IDataStorage>();
        var row = await MessagingCommandHost.InboxFailureAsync(storageB, "order-1", AbortToken);
        using var client = MessagingCommandHost.Client(nodeA);
        var cookie = await MessagingCommandHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var run = await _ReExecuteAsync(client, cookie, _IdOnB(row));

        // then
        run.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await _DetailAsync(run)).Should().Contain("insecure peer");
        _network.Calls.Should().BeEmpty();
        nodeA
            .Services.GetRequiredService<PeerDirectory>()
            .StatusOf("node-b")!
            .State.Should()
            .Be(PeerState.InsecurePeer);
        _logB.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task should_call_an_http_peer_when_insecure_peers_are_allowed()
    {
        // given
        _discoveryA.Set(ForwardingHost.Entry("node-b", "http://node-b"));
        await using var nodeB = await _StartBAsync(_Operate);
        await using var nodeA = await _StartAAsync(options => options.Forwarding.AllowInsecurePeers = true);
        var storageB = nodeB.Services.GetRequiredService<IDataStorage>();
        var row = await MessagingCommandHost.InboxFailureAsync(storageB, "order-1", AbortToken);
        using var client = MessagingCommandHost.Client(nodeA);
        var cookie = await MessagingCommandHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var run = await _ReExecuteAsync(client, cookie, _IdOnB(row));

        // then
        run.StatusCode.Should().Be(HttpStatusCode.OK);
        _network.Calls.Should().NotBeEmpty().And.OnlyContain(sent => sent.Uri.Scheme == Uri.UriSchemeHttp);
        _logB.Entries.Should().ContainSingle().Which.Actor.Should().Be("ops via node-a");
    }

    [Fact]
    public async Task should_run_on_this_node_and_call_no_peer_when_discovery_lists_this_node_itself()
    {
        // given
        _discoveryA.Set(ForwardingHost.Entry("node-a", "https://node-a"));
        await using var nodeA = await _StartAAsync(_Operate);
        _network.Add("node-a", nodeA);
        var storageA = nodeA.Services.GetRequiredService<IDataStorage>();
        var row = await MessagingCommandHost.InboxFailureAsync(storageA, "order-1", AbortToken);
        using var client = MessagingCommandHost.Client(nodeA);
        var cookie = await MessagingCommandHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var run = await _ReExecuteAsync(client, cookie, $"node-a~primary~{row.StorageId:D}");

        // then
        run.StatusCode.Should().Be(HttpStatusCode.OK);
        _network.Calls.Should().BeEmpty();
        _logA.Entries.Should().ContainSingle().Which.Actor.Should().Be("ops");
    }

    private static void _Operate(MessagingDashboardOptions options) =>
        options.ForwardedAccess = MessagingForwardedAccess.Operate;

    private static void _PolicyMode(MessagingDashboardOptions options)
    {
        options.Forwarding.Key = null;
        options.Forwarding.Policy = PeerIdentityHandler.PolicyName;
        options.ForwardedAccess = MessagingForwardedAccess.Operate;
    }

    private static void _PresentIdentity(IServiceCollection services, string name)
    {
        PeerIdentityHandler.AddTo(services);
        services.AddHttpClient(ForwardingProtocol.ClientName).AddHttpMessageHandler(() => new PeerIdentityHeader(name));
    }

    private async Task<WebApplication> _StartBAsync(
        Action<MessagingDashboardOptions>? options = null,
        Action<IServiceCollection>? configure = null
    )
    {
        var app = await ForwardingHost.StartAsync(
            "node-b",
            _network,
            _discoveryB,
            _time,
            _logB,
            AbortToken,
            options,
            configure
        );
        _network.Add("node-b", app);

        return app;
    }

    private Task<WebApplication> _StartAAsync(
        Action<MessagingDashboardOptions>? options = null,
        Action<IServiceCollection>? configure = null
    ) => ForwardingHost.StartAsync("node-a", _network, _discoveryA, _time, _logA, AbortToken, options, configure);

    private static string _IdOnB(MediumMessage row) => $"node-b~primary~{row.StorageId:D}";

    private static object _ReExecuteBody(string id) => new { ids = new[] { id }, confirmed = true };

    private Task<HttpResponseMessage> _ReExecuteAsync(HttpClient client, string cookie, string id) =>
        MessagingCommandHost.PostAsync(
            client,
            $"{_Commands}/{MessagingCommands.ReExecuteId}",
            _ReExecuteBody(id),
            cookie,
            AbortToken
        );

    /// <summary>Sends a re-execute straight to a node as node A would forward it.</summary>
    private async Task<HttpResponseMessage> _SendAsPeerAsync(
        HttpClient client,
        object body,
        string? key,
        string access = ForwardingProtocol.AccessOperate,
        string? cookie = null
    )
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri($"{_Commands}/{MessagingCommands.ReExecuteId}", UriKind.Relative)
        )
        {
            Content = System.Net.Http.Json.JsonContent.Create(body),
        };
        request.Headers.TryAddWithoutValidation(ForwardingProtocol.HopHeader, "node-a");
        request.Headers.TryAddWithoutValidation(ForwardingProtocol.ActorHeader, "ops");
        request.Headers.TryAddWithoutValidation(ForwardingProtocol.AccessHeader, access);
        request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
        request.Headers.TryAddWithoutValidation("X-Headless-Dashboard-Channel", "ui");

        if (key is not null)
        {
            request.Headers.TryAddWithoutValidation(ForwardingProtocol.KeyHeader, key);
        }

        if (cookie is not null)
        {
            request.Headers.TryAddWithoutValidation("Cookie", cookie);
        }

        return await client.SendAsync(request, AbortToken);
    }

    private async Task<string?> _DetailAsync(HttpResponseMessage response) =>
        (await MessagingCommandHost.ReadAsync(response, AbortToken)).GetProperty("detail").GetString();

    private static string? _Refusal(HttpResponseMessage response) =>
        response.Headers.TryGetValues(ForwardingProtocol.RefusalHeader, out var values) ? values.First() : null;

    private async Task<long> _ReceivedCountAsync(IDataStorage storage) =>
        (
            await storage
                .GetMonitoringApi()
                .CountMessagesAsync(new MessageListQuery { MessageType = MessageType.Subscribe }, 10, AbortToken)
        ).Count;
}
