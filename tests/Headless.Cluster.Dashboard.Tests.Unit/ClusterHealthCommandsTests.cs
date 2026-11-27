// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Cluster;
using Headless.Dashboard;
using Headless.Hosting;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Tests;

/// <summary>
/// The probe commands over a monitor a test controls: a monitor that fails, and a probe cancelled part-way through,
/// each leave the error to the shell, which records the attempt as failed, and lose no check already probed.
/// </summary>
public sealed class ClusterHealthCommandsTests : TestBase
{
    private static readonly DateTimeOffset _Completed = new(2026, 11, 27, 10, 0, 0, TimeSpan.Zero);

    private readonly ScriptedMonitor _monitor = new();
    private readonly RecordingPublisher _publisher = new();

    [Fact]
    public async Task should_let_a_failing_monitor_propagate_and_publish_nothing()
    {
        // given
        _monitor.ProbeAll = static _ => throw new InvalidOperationException("The monitor was disposed.");
        var commands = _Commands();

        // when
        var run = () =>
            commands.ExecuteAsync(_Request(ClusterHealthCommands.ProbeAllId, Environment.MachineName), AbortToken);

        // then
        await run.Should().ThrowAsync<InvalidOperationException>();
        _publisher.Changes.Should().BeEmpty();
    }

    [Fact]
    public async Task should_let_a_probe_cancelled_mid_run_propagate_after_publishing_the_checks_already_probed()
    {
        // given
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        _monitor.Probe = (name, token) =>
        {
            if (string.Equals(name, "second", StringComparison.Ordinal))
            {
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
            }

            _monitor.Record(name);

            return _Report(name);
        };
        var commands = _Commands();

        // when
        var run = () =>
            commands.ExecuteAsync(_Request(ClusterHealthCommands.ProbeId, "first", "second"), cancellation.Token);

        // then
        await run.Should().ThrowAsync<OperationCanceledException>();
        _publisher.Changes.Should().ContainSingle().Which.Target.Id.Should().Be("first");
    }

    private ClusterHealthCommands _Commands()
    {
        var provider = new ServiceCollection().AddSingleton<IHealthCheckMonitor>(_monitor).BuildServiceProvider();

        return new ClusterHealthCommands("cluster", new ClusterServices(provider), _publisher);
    }

    private static DashboardCommandRequest _Request(string commandId, params string[] ids) =>
        new()
        {
            CommandId = commandId,
            Ids = ids,
            Channel = DashboardChannels.Ui,
            User = new System.Security.Claims.ClaimsPrincipal(),
        };

    private static HealthReport _Report(string name) =>
        new(
            new Dictionary<string, HealthReportEntry>(StringComparer.Ordinal)
            {
                [name] = new(HealthStatus.Healthy, null, TimeSpan.Zero, null, null),
            },
            TimeSpan.Zero
        );

    /// <summary>A monitor whose probes a test scripts, and whose snapshot holds the results it records.</summary>
    private sealed class ScriptedMonitor : IHealthCheckMonitor
    {
        private readonly Lock _lock = new();
        private readonly Dictionary<string, HealthCheckProbeResult> _results = [with(StringComparer.Ordinal)];

        public Func<CancellationToken, HealthReport> ProbeAll { get; set; } =
            static _ => throw new NotSupportedException();

        public Func<string, CancellationToken, HealthReport?> Probe { get; set; } =
            static (_, _) => throw new NotSupportedException();

        public void Record(string name)
        {
            lock (_lock)
            {
                _results[name] = new HealthCheckProbeResult
                {
                    Status = HealthStatus.Healthy,
                    Duration = TimeSpan.Zero,
                    Description = null,
                    CompletedAt = _Completed,
                };
            }
        }

        public IReadOnlyList<HealthCheckDescriptor> GetChecks() => [.. _Histories().Select(static h => h.Descriptor)];

        public Task<HealthReport> ProbeAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(ProbeAll(cancellationToken));

        public Task<HealthReport?> ProbeAsync(string name, CancellationToken cancellationToken = default) =>
            Task.FromResult(Probe(name, cancellationToken));

        public HealthCheckSnapshot GetSnapshot() => new(null, _Histories());

        private List<HealthCheckHistory> _Histories()
        {
            lock (_lock)
            {
                return
                [
                    .. _results.Select(static pair => new HealthCheckHistory(
                        new HealthCheckDescriptor
                        {
                            Name = pair.Key,
                            Tags = [],
                            FailureStatus = HealthStatus.Unhealthy,
                            Timeout = null,
                            IsLiveness = false,
                        },
                        [pair.Value]
                    )),
                ];
            }
        }
    }
}
