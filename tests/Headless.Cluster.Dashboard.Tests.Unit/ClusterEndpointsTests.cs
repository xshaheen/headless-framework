// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Coordination;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Tests;

/// <summary>
/// The module's membership reads on a real host over a fake membership store: the summary of both halves, the node
/// list and one node, this node's event log, and metadata agreement, each in the shape the SPA reads.
/// </summary>
public sealed class ClusterEndpointsTests : TestBase
{
    private const string _Api = ClusterDashboardHost.Api;
    private static readonly DateTimeOffset _Beat = new(2026, 11, 27, 9, 59, 55, TimeSpan.Zero);

    private readonly FakeMembership _membership = new() { Identity = NodeIdentity.Parse("orders-api-0@14") };

    public ClusterEndpointsTests()
    {
        _membership.SetSnapshot(
            FakeMembership.Node("orders-worker-2@3", NodeLivenessState.Dead, metadata: ("app.version", "4.1.0")),
            FakeMembership.Node("orders-api-1@2", NodeLivenessState.Alive, "api", _Beat, ("app.version", "4.2.1")),
            FakeMembership.Node("orders-api-0@14", NodeLivenessState.Alive, "api", _Beat, ("app.version", "4.2.1")),
            FakeMembership.Node(
                "orders-worker-1@5",
                NodeLivenessState.Suspected,
                "worker",
                _Beat,
                ("app.version", "4.2.0"),
                ("pod", "w1")
            ),
            FakeMembership.Node("orders-worker-0@7", NodeLivenessState.Alive, "worker", _Beat, ("app.version", "4.2.1"))
        );
    }

    [Fact]
    public async Task should_list_every_incarnation_in_id_order_and_mark_the_serving_node()
    {
        // given
        await using var app = await ClusterDashboardHost.StartAsync(AbortToken, membership: _membership);

        // when
        var body = await ClusterDashboardHost.GetJsonAsync(app, _Api + "/nodes", AbortToken);

        // then
        var items = body.GetProperty("items").EnumerateArray().ToList();
        items
            .Select(static item => item.GetProperty("id").GetString())
            .Should()
            .Equal("orders-api-0@14", "orders-api-1@2", "orders-worker-0@7", "orders-worker-1@5", "orders-worker-2@3");
        var local = items[0];
        local.GetProperty("isLocal").GetBoolean().Should().BeTrue();
        local.GetProperty("nodeId").GetString().Should().Be("orders-api-0");
        local.GetProperty("incarnation").GetInt64().Should().Be(14);
        local.GetProperty("state").GetString().Should().Be("Alive");
        local.GetProperty("role").GetString().Should().Be("api");
        local.GetProperty("lastHeartbeatAt").GetDateTimeOffset().Should().Be(_Beat);
        local.GetProperty("ownerTag").GetString().Should().Be("orders/orders-api-0@14");
        local.GetProperty("metadata").GetProperty("app.version").GetString().Should().Be("4.2.1");
        items.Skip(1).Should().OnlyContain(static item => !item.GetProperty("isLocal").GetBoolean());
    }

    [Fact]
    public async Task should_filter_the_list_by_state_and_refuse_a_state_that_is_not_served()
    {
        // given
        await using var app = await ClusterDashboardHost.StartAsync(AbortToken, membership: _membership);
        using var client = ClusterDashboardHost.Client(app);
        var cookie = await ClusterDashboardHost.SignInAsync(client, "ops", AbortToken);

        // when
        var suspected = await ClusterDashboardHost.GetJsonAsync(app, _Api + "/nodes?state=suspected", AbortToken);
        using var left = await ClusterDashboardHost.GetAsync(client, _Api + "/nodes?state=Left", cookie, AbortToken);
        using var number = await ClusterDashboardHost.GetAsync(client, _Api + "/nodes?state=1", cookie, AbortToken);

        // then
        suspected
            .GetProperty("items")
            .EnumerateArray()
            .Select(static item => item.GetProperty("id").GetString())
            .Should()
            .Equal("orders-worker-1@5");
        left.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ClusterDashboardHost.ReadAsync(left, AbortToken))
            .GetProperty("rules")
            .GetProperty("state")
            .GetString()
            .Should()
            .Be("enum");
        number.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task should_return_one_node_with_its_own_events_and_how_live_nodes_agree_on_its_keys()
    {
        // given
        _membership.SetEntries(
            _Entry(1, new NodeJoined(NodeIdentity.Parse("orders-worker-1@5")), NodeLivenessState.Alive, first: true),
            _Entry(2, new NodeJoined(NodeIdentity.Parse("orders-api-1@2")), NodeLivenessState.Alive),
            _Entry(3, new NodeSuspected(NodeIdentity.Parse("orders-worker-1@5")), NodeLivenessState.Suspected)
        );
        await using var app = await ClusterDashboardHost.StartAsync(AbortToken, membership: _membership);

        // when
        var body = await ClusterDashboardHost.GetJsonAsync(app, _Api + "/nodes/orders-worker-1@5", AbortToken);

        // then
        body.GetProperty("node").GetProperty("id").GetString().Should().Be("orders-worker-1@5");
        body.GetProperty("events")
            .EnumerateArray()
            .Select(static entry => (entry.GetProperty("sequence").GetInt64(), entry.GetProperty("kind").GetString()))
            .Should()
            .Equal((3L, "NodeSuspected"), (1L, "NodeJoined"));
        var version = body.GetProperty("agreement")
            .EnumerateArray()
            .Single(static key =>
                string.Equals(key.GetProperty("key").GetString(), "app.version", StringComparison.Ordinal)
            );
        version.GetProperty("isUnanimous").GetBoolean().Should().BeFalse();
        version.GetProperty("value").GetString().Should().Be("4.2.0");
        // The dead node's 4.1.0 is not compared: only the four alive or suspected nodes are.
        version.GetProperty("matches").GetInt32().Should().Be(1);
        version.GetProperty("compared").GetInt32().Should().Be(4);
        var other = version.GetProperty("groups").EnumerateArray().Should().ContainSingle().Subject;
        other.GetProperty("value").GetString().Should().Be("4.2.1");
        other
            .GetProperty("nodes")
            .EnumerateArray()
            .Select(static node => node.GetString())
            .Should()
            .Equal("orders-api-0@14", "orders-api-1@2", "orders-worker-0@7");
    }

    [Fact]
    public async Task should_answer_404_for_an_identity_that_left_the_snapshot_or_does_not_parse()
    {
        // given
        await using var app = await ClusterDashboardHost.StartAsync(AbortToken, membership: _membership);
        using var client = ClusterDashboardHost.Client(app);
        var cookie = await ClusterDashboardHost.SignInAsync(client, "ops", AbortToken);
        using var present = await ClusterDashboardHost.GetAsync(
            client,
            _Api + "/nodes/orders-worker-2@3",
            cookie,
            AbortToken
        );
        _membership.SetSnapshot(FakeMembership.Node("orders-api-0@14"));

        // when
        using var gone = await ClusterDashboardHost.GetAsync(
            client,
            _Api + "/nodes/orders-worker-2@3",
            cookie,
            AbortToken
        );
        using var garbage = await ClusterDashboardHost.GetAsync(
            client,
            _Api + "/nodes/not-an-identity",
            cookie,
            AbortToken
        );

        // then
        present.StatusCode.Should().Be(HttpStatusCode.OK);
        gone.StatusCode.Should().Be(HttpStatusCode.NotFound);
        garbage.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task should_return_this_nodes_event_log_newest_first_labelled_with_the_observer_and_local_clock()
    {
        // given
        _membership.SetEntries(
            _Entry(1, new NodeJoined(NodeIdentity.Parse("orders-api-1@2")), NodeLivenessState.Alive, first: true),
            _Entry(2, new NodeLeft(NodeIdentity.Parse("orders-worker-3@1")), state: null)
        );
        await using var app = await ClusterDashboardHost.StartAsync(AbortToken, membership: _membership);

        // when
        var body = await ClusterDashboardHost.GetJsonAsync(app, _Api + "/events", AbortToken);

        // then
        body.GetProperty("observer").GetString().Should().Be("orders-api-0@14");
        body.GetProperty("clock").GetString().Should().Be("local");
        var items = body.GetProperty("items").EnumerateArray().ToList();
        items.Select(static item => item.GetProperty("sequence").GetInt64()).Should().Equal(2, 1);
        items[0].GetProperty("kind").GetString().Should().Be("NodeLeft");
        items[0].GetProperty("node").GetString().Should().Be("orders-worker-3@1");
        items[0].GetProperty("state").ValueKind.Should().Be(JsonValueKind.Null);
        items[0].GetProperty("isFirstObservation").GetBoolean().Should().BeFalse();
        items[1].GetProperty("isFirstObservation").GetBoolean().Should().BeTrue();
        items[1].GetProperty("state").GetString().Should().Be("Alive");
    }

    [Fact]
    public async Task should_compare_every_key_a_live_node_carries_and_leave_dead_nodes_out()
    {
        // given
        _membership.SetSnapshot(
            FakeMembership.Node("a@1", metadata: [("b", "x"), ("a", "1")]),
            FakeMembership.Node("b@1", metadata: ("a", "1")),
            FakeMembership.Node("c@1", NodeLivenessState.Dead, metadata: [("a", "2"), ("z", "dead-only")])
        );
        await using var app = await ClusterDashboardHost.StartAsync(AbortToken, membership: _membership);

        // when
        var body = await ClusterDashboardHost.GetJsonAsync(app, _Api + "/metadata-agreement", AbortToken);

        // then
        var keys = body.GetProperty("keys").EnumerateArray().ToList();
        keys.Select(static key => key.GetProperty("key").GetString()).Should().Equal("a", "b");
        keys[0].GetProperty("isUnanimous").GetBoolean().Should().BeTrue();
        keys[0].GetProperty("groups").EnumerateArray().Should().ContainSingle();
        keys[1].GetProperty("isUnanimous").GetBoolean().Should().BeFalse();
        var groups = keys[1].GetProperty("groups").EnumerateArray().ToList();
        groups
            .Select(static group => group.GetProperty("value").ValueKind)
            .Should()
            .Equal(JsonValueKind.String, JsonValueKind.Null);
    }

    [Fact]
    public async Task should_report_no_coordination_and_a_registered_monitor_on_a_health_only_host()
    {
        // given a host that registers its health checks before the module, with no Coordination
        await using var app = await ClusterDashboardHost.StartAsync(
            AbortToken,
            before: static services =>
                services.AddHealthChecks().AddCheck("db", static () => HealthCheckResult.Healthy())
        );
        using var client = ClusterDashboardHost.Client(app);
        var cookie = await ClusterDashboardHost.SignInAsync(client, "ops", AbortToken);

        // when
        var summary = await ClusterDashboardHost.GetJsonAsync(app, _Api + "/summary", AbortToken);
        using var nodes = await ClusterDashboardHost.GetAsync(client, _Api + "/nodes", cookie, AbortToken);

        // then
        summary.GetProperty("coordination").ValueKind.Should().Be(JsonValueKind.Null);
        summary.GetProperty("health").GetProperty("registered").GetBoolean().Should().BeTrue();
        summary.GetProperty("health").GetProperty("monitor").GetBoolean().Should().BeTrue();
        summary.GetProperty("host").GetString().Should().Be(Environment.MachineName);
        nodes.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await nodes.Content.ReadAsStringAsync(AbortToken)).Should().Contain("AddHeadlessCoordination");
    }

    [Fact]
    public async Task should_report_a_registered_health_half_without_a_monitor_when_checks_come_after_the_module()
    {
        // given
        await using var app = await ClusterDashboardHost.StartAsync(
            AbortToken,
            after: static services =>
                services.AddHealthChecks().AddCheck("db", static () => HealthCheckResult.Healthy())
        );

        // when
        var summary = await ClusterDashboardHost.GetJsonAsync(app, _Api + "/summary", AbortToken);

        // then
        summary.GetProperty("coordination").ValueKind.Should().Be(JsonValueKind.Null);
        summary.GetProperty("health").GetProperty("registered").GetBoolean().Should().BeTrue();
        summary.GetProperty("health").GetProperty("monitor").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task should_describe_the_coordination_settings_and_this_node_in_the_summary()
    {
        // given
        await using var app = await ClusterDashboardHost.StartAsync(AbortToken, membership: _membership);

        // when
        var summary = await ClusterDashboardHost.GetJsonAsync(app, _Api + "/summary", AbortToken);

        // then
        summary.GetProperty("host").GetString().Should().Be("orders-api-0@14");
        summary.GetProperty("health").ValueKind.Should().Be(JsonValueKind.Null);
        var coordination = summary.GetProperty("coordination");
        coordination.GetProperty("clusterName").GetString().Should().Be("orders");
        coordination.GetProperty("providerName").GetString().Should().Be("PostgreSql");
        coordination.GetProperty("localNode").GetString().Should().Be("orders-api-0@14");
        coordination.GetProperty("heartbeatIntervalMilliseconds").GetInt64().Should().Be(5000);
        coordination.GetProperty("suspicionThresholdMilliseconds").GetInt64().Should().Be(15000);
        coordination.GetProperty("deadThresholdMilliseconds").GetInt64().Should().Be(30000);
        coordination.GetProperty("deadRetentionWindowMilliseconds").GetInt64().Should().Be(30000);
        coordination.GetProperty("selfFenceMarginMilliseconds").GetInt64().Should().Be(5000);
        coordination.GetProperty("membershipHeartbeatEnabled").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task should_let_a_viewer_read_and_refuse_an_anonymous_request()
    {
        // given
        await using var app = await ClusterDashboardHost.StartAsync(AbortToken, membership: _membership);
        using var client = ClusterDashboardHost.Client(app);

        // when
        var viewer = await ClusterDashboardHost.GetJsonAsync(app, _Api + "/nodes", AbortToken, role: "viewer");
        using var anonymous = await ClusterDashboardHost.GetAsync(client, _Api + "/nodes", cookie: null, AbortToken);

        // then
        viewer.GetProperty("items").GetArrayLength().Should().Be(5);
        anonymous.IsSuccessStatusCode.Should().BeFalse();
    }

    private static MembershipEventLogEntry _Entry(
        long sequence,
        NodeMembershipEvent membershipEvent,
        NodeLivenessState? state,
        bool first = false
    ) =>
        new()
        {
            Sequence = sequence,
            Event = membershipEvent,
            ObservedAt = _Beat.AddSeconds(sequence),
            State = state,
            LastHeartbeatAt = state is null ? null : _Beat,
            IsFirstObservation = first,
        };
}
