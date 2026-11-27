// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Dashboard;
using Headless.Hosting;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Tests;

/// <summary>
/// The probe commands through the shell's command endpoints: what a probe answers, that the shell logs each run, who
/// may run it, and that each probed check goes out on this node's streams.
/// </summary>
public sealed class ClusterCommandsTests : TestBase
{
    private const string _Commands = ClusterDashboardHost.Commands;

    private readonly HealthToggle _dependency = new() { Status = HealthStatus.Degraded };
    private readonly HealthToggle _scheduler = new();
    private readonly RecordingOperatorLog _log = new();
    private readonly RecordingPublisher _publisher = new();

    [Fact]
    public async Task should_probe_one_check_answer_its_status_and_log_one_run()
    {
        // given
        await using var app = await _StartAsync();
        using var client = ClusterDashboardHost.Client(app);
        var cookie = await ClusterDashboardHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var response = await ClusterDashboardHost.PostAsync(
            client,
            _Commands + "/cluster.probe",
            new { ids = new[] { "sandbox-dependency" } },
            cookie,
            AbortToken
        );

        // then
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(AbortToken));
        var row = (await ClusterDashboardHost.ReadAsync(response, AbortToken))
            .GetProperty("rows")
            .EnumerateArray()
            .Should()
            .ContainSingle()
            .Subject;
        row.GetProperty("id").GetString().Should().Be("sandbox-dependency");
        row.GetProperty("outcome").GetString().Should().BeEquivalentTo("Done");
        row.GetProperty("reason").GetString().Should().Be("Degraded");
        _log.Entries.Should()
            .ContainSingle()
            .Which.Should()
            .Match<OperatorLogEntry>(entry =>
                entry.Action == OperatorLogAction.Run
                && entry.CommandId == "cluster.probe"
                && entry.Actor == "ops"
                && entry.Ids.SequenceEqual(new[] { "sandbox-dependency" })
            );
        _dependency.Runs.Should().Be(1);
        _scheduler.Runs.Should().Be(0);
    }

    [Fact]
    public async Task should_skip_a_name_no_check_carries()
    {
        // given
        await using var app = await _StartAsync();
        using var client = ClusterDashboardHost.Client(app);
        var cookie = await ClusterDashboardHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var response = await ClusterDashboardHost.PostAsync(
            client,
            _Commands + "/cluster.probe",
            new { ids = new[] { "missing" } },
            cookie,
            AbortToken
        );

        // then
        var row = (await ClusterDashboardHost.ReadAsync(response, AbortToken)).GetProperty("rows")[0];
        row.GetProperty("outcome").GetString().Should().BeEquivalentTo("Skipped");
        row.GetProperty("reason").GetString().Should().Be("No check named 'missing'");
    }

    [Fact]
    public async Task should_probe_every_check_with_one_row_for_the_host()
    {
        // given
        await using var app = await _StartAsync();
        using var client = ClusterDashboardHost.Client(app);
        var cookie = await ClusterDashboardHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var response = await ClusterDashboardHost.PostAsync(
            client,
            _Commands + "/cluster.probe-all",
            new { ids = new[] { Environment.MachineName } },
            cookie,
            AbortToken
        );
        var health = await ClusterDashboardHost.GetJsonAsync(app, ClusterDashboardHost.Api + "/health", AbortToken);

        // then
        var row = (await ClusterDashboardHost.ReadAsync(response, AbortToken))
            .GetProperty("rows")
            .EnumerateArray()
            .Should()
            .ContainSingle()
            .Subject;
        row.GetProperty("id").GetString().Should().Be(Environment.MachineName);
        row.GetProperty("reason").GetString().Should().Be("Degraded · 2 checks");
        health.GetProperty("status").GetString().Should().Be("Degraded");
        health.GetProperty("totals").GetProperty("degraded").GetInt32().Should().Be(1);
        _log.Entries.Should().ContainSingle(static entry => entry.CommandId == "cluster.probe-all");
    }

    [Theory]
    [InlineData("replica-b")]
    [InlineData(null)]
    public async Task should_refuse_probe_all_unrun_when_it_does_not_name_exactly_the_serving_host(string? other)
    {
        // given
        await using var app = await _StartAsync();
        using var client = ClusterDashboardHost.Client(app);
        var cookie = await ClusterDashboardHost.SignInAsync(client, "ops", AbortToken);
        // another replica's label, or the serving host together with another one
        var ids = other is null ? new[] { Environment.MachineName, "replica-b" } : [other];

        // when
        using var response = await ClusterDashboardHost.PostAsync(
            client,
            _Commands + "/cluster.probe-all",
            new { ids },
            cookie,
            AbortToken
        );

        // then
        var body = await response.Content.ReadAsStringAsync(AbortToken);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict, body);
        body.Should().Contain($"This request reached host '{Environment.MachineName}'");
        _dependency.Runs.Should().Be(0);
        _scheduler.Runs.Should().Be(0);
        _log.Entries.Should().BeEmpty();
        _publisher.Changes.Should().BeEmpty();
    }

    [Fact]
    public async Task should_disable_probe_all_for_a_host_other_than_the_serving_one_naming_the_serving_host()
    {
        // given
        await using var app = await _StartAsync();
        using var client = ClusterDashboardHost.Client(app);
        var cookie = await ClusterDashboardHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var response = await ClusterDashboardHost.GetAsync(
            client,
            _Commands + "/allowed?kind=health&id=replica-b",
            cookie,
            AbortToken
        );

        // then
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(AbortToken));
        var command = (await ClusterDashboardHost.ReadAsync(response, AbortToken))
            .GetProperty("objects")[0]
            .GetProperty("commands")
            .EnumerateArray()
            .Single(static command =>
                string.Equals(command.GetProperty("id").GetString(), "cluster.probe-all", StringComparison.Ordinal)
            );
        command.GetProperty("allowed").GetBoolean().Should().BeFalse();
        command
            .GetProperty("disabledReason")
            .GetString()
            .Should()
            .Contain($"This request reached host '{Environment.MachineName}', not 'replica-b'");
    }

    [Fact]
    public async Task should_publish_one_check_change_per_probed_check_with_a_later_version_each_time()
    {
        // given
        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(ClusterDashboardHost.Now);
        await using var app = await _StartAsync(time);
        using var client = ClusterDashboardHost.Client(app);
        var cookie = await ClusterDashboardHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var all = await ClusterDashboardHost.PostAsync(
            client,
            _Commands + "/cluster.probe-all",
            new { ids = new[] { Environment.MachineName } },
            cookie,
            AbortToken
        );
        time.Advance(TimeSpan.FromSeconds(1));
        using var one = await ClusterDashboardHost.PostAsync(
            client,
            _Commands + "/cluster.probe",
            new { ids = new[] { "SANDBOX-DEPENDENCY" } },
            cookie,
            AbortToken
        );

        // then
        var changes = _publisher.Changes;
        changes.Should().HaveCount(3);
        changes
            .Should()
            .OnlyContain(static change => change.Target.Module == "cluster" && change.Target.Kind == "check");
        changes.Should().OnlyContain(static change => change.AffectsAttention);
        changes
            .Take(2)
            .Select(static change => change.Target.Id)
            .Should()
            .BeEquivalentTo("jobs-scheduler", "sandbox-dependency");
        var first = changes.First(static change =>
            string.Equals(change.Target.Id, "sandbox-dependency", StringComparison.Ordinal)
        );
        changes[2].Target.Id.Should().Be("sandbox-dependency");
        string.CompareOrdinal(changes[2].Version, first.Version).Should().BePositive();
        changes[2].Version.Should().HaveLength(19);
    }

    [Fact]
    public async Task should_refuse_a_viewer_and_log_nothing()
    {
        // given
        await using var app = await _StartAsync();
        using var client = ClusterDashboardHost.Client(app);
        var cookie = await ClusterDashboardHost.SignInAsync(client, "viewer", AbortToken);

        // when
        using var response = await ClusterDashboardHost.PostAsync(
            client,
            _Commands + "/cluster.probe-all",
            new { ids = new[] { Environment.MachineName } },
            cookie,
            AbortToken
        );

        // then
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _log.Entries.Should().BeEmpty();
        _dependency.Runs.Should().Be(0);
    }

    [Fact]
    public async Task should_disable_both_commands_and_refuse_a_run_when_the_monitor_is_missing()
    {
        // given a host whose health checks have no monitor registered
        await using var app = await ClusterDashboardHost.StartAsync(
            AbortToken,
            log: _log,
            after: services =>
            {
                _dependency.AddTo(services.AddHealthChecks(), "sandbox-dependency");
                services.RemoveAll<IHealthCheckMonitor>();
            }
        );
        using var client = ClusterDashboardHost.Client(app);
        var cookie = await ClusterDashboardHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var allowedCheck = await ClusterDashboardHost.GetAsync(
            client,
            _Commands + "/allowed?kind=check&id=sandbox-dependency",
            cookie,
            AbortToken
        );
        using var allowedHost = await ClusterDashboardHost.GetAsync(
            client,
            _Commands + "/allowed?kind=health&id=" + Uri.EscapeDataString(Environment.MachineName),
            cookie,
            AbortToken
        );
        using var run = await ClusterDashboardHost.PostAsync(
            client,
            _Commands + "/cluster.probe",
            new { ids = new[] { "sandbox-dependency" } },
            cookie,
            AbortToken
        );

        // then
        foreach (
            var (response, commandId) in new[] { (allowedCheck, "cluster.probe"), (allowedHost, "cluster.probe-all") }
        )
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(AbortToken));
            var command = (await ClusterDashboardHost.ReadAsync(response, AbortToken))
                .GetProperty("objects")[0]
                .GetProperty("commands")
                .EnumerateArray()
                .Single(command =>
                    string.Equals(command.GetProperty("id").GetString(), commandId, StringComparison.Ordinal)
                );
            command.GetProperty("allowed").GetBoolean().Should().BeFalse();
            command.GetProperty("disabledReason").GetString().Should().Contain("AddHeadlessHealthCheckMonitor");
        }

        run.StatusCode.Should().Be(HttpStatusCode.Conflict);
        _log.Entries.Should().BeEmpty();
        _dependency.Runs.Should().Be(0);
    }

    [Fact]
    public async Task should_allow_both_commands_for_an_operator_when_the_monitor_is_registered()
    {
        // given
        await using var app = await _StartAsync();
        using var client = ClusterDashboardHost.Client(app);
        var cookie = await ClusterDashboardHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var allowedCheck = await ClusterDashboardHost.GetAsync(
            client,
            _Commands + "/allowed?kind=check&id=sandbox-dependency",
            cookie,
            AbortToken
        );
        using var allowedHost = await ClusterDashboardHost.GetAsync(
            client,
            _Commands + "/allowed?kind=health&id=" + Uri.EscapeDataString(Environment.MachineName),
            cookie,
            AbortToken
        );

        // then
        foreach (
            var (response, commandId) in new[] { (allowedCheck, "cluster.probe"), (allowedHost, "cluster.probe-all") }
        )
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(AbortToken));
            var command = (await ClusterDashboardHost.ReadAsync(response, AbortToken))
                .GetProperty("objects")[0]
                .GetProperty("commands")
                .EnumerateArray()
                .Single(command =>
                    string.Equals(command.GetProperty("id").GetString(), commandId, StringComparison.Ordinal)
                );
            command.GetProperty("allowed").GetBoolean().Should().BeTrue();
        }
    }

    private Task<WebApplication> _StartAsync(Microsoft.Extensions.Time.Testing.FakeTimeProvider? time = null) =>
        ClusterDashboardHost.StartAsync(
            AbortToken,
            time: time,
            log: _log,
            before: services =>
            {
                var checks = services.AddHealthChecks();
                _dependency.AddTo(checks, "sandbox-dependency");
                _scheduler.AddTo(checks, "jobs-scheduler");
            },
            after: services => services.Replace(ServiceDescriptor.Singleton<IDashboardEventPublisher>(_publisher))
        );
}
