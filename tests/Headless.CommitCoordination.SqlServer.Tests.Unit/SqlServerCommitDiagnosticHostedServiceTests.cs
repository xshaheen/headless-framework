// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination;
using Headless.CommitCoordination.SqlServer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.
public sealed class SqlServerCommitDiagnosticHostedServiceTests
{
    [Fact]
    public async Task should_register_default_probe_options_for_di()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILogger<SqlServerCommitDiagnosticObserver>>(
            NullLogger<SqlServerCommitDiagnosticObserver>.Instance
        );
        services.AddSqlServerCommitCoordination();

        await using var provider = services.BuildServiceProvider();
        var hostedService = provider
            .GetServices<IHostedService>()
            .OfType<SqlServerCommitDiagnosticHostedService>()
            .Single();

        await hostedService.StartAsync(TestContext.Current.CancellationToken);
        await hostedService.StopAsync(TestContext.Current.CancellationToken);

        provider
            .GetRequiredService<SqlServerCommitDiagnosticProbeState>()
            .Status.Should()
            .Be(SqlServerCommitDiagnosticProbeStatus.Degraded);
    }

    [Fact]
    public async Task should_skip_probe_when_diagnostic_probe_mode_is_disabled()
    {
        var probe = new RecordingProbe(SqlServerCommitDiagnosticProbeResult.Failure("should not run"));
        var state = new SqlServerCommitDiagnosticProbeState();

        await using var service = _CreateService(
            probe,
            state,
            new SqlServerCommitCoordinationOptions { DiagnosticProbeMode = SqlServerCommitDiagnosticProbeMode.Disabled }
        );

        await service.StartAsync(TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        probe.Calls.Should().Be(0);
        state.Status.Should().Be(SqlServerCommitDiagnosticProbeStatus.Skipped);
    }

    [Fact]
    public async Task should_record_degraded_state_without_throwing_when_probe_fails_in_warn_mode()
    {
        var failure = new InvalidOperationException("payload shape changed");
        var probe = new RecordingProbe(
            SqlServerCommitDiagnosticProbeResult.Failure("SQL diagnostics unavailable.", failure)
        );
        var state = new SqlServerCommitDiagnosticProbeState();
        await using var service = _CreateService(probe, state, new SqlServerCommitCoordinationOptions());

        await service.StartAsync(TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        probe.Calls.Should().Be(1);
        state.Status.Should().Be(SqlServerCommitDiagnosticProbeStatus.Degraded);
        state.Exception.Should().BeSameAs(failure);
    }

    [Fact]
    public async Task should_fail_startup_when_probe_fails_in_strict_mode()
    {
        var failure = new InvalidOperationException("payload shape changed");

        var probe = new RecordingProbe(
            SqlServerCommitDiagnosticProbeResult.Failure("SQL diagnostics unavailable.", failure)
        );

        var state = new SqlServerCommitDiagnosticProbeState();

        await using var service = _CreateService(
            probe,
            state,
            new SqlServerCommitCoordinationOptions { DiagnosticProbeMode = SqlServerCommitDiagnosticProbeMode.Strict }
        );

        await service
            .Invoking(x => x.StartAsync(TestContext.Current.CancellationToken))
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("SQL diagnostics unavailable.");

        probe.Calls.Should().Be(1);
        state.Status.Should().Be(SqlServerCommitDiagnosticProbeStatus.Failed);
        state.Exception.Should().BeSameAs(failure);
    }

    [Fact]
    public async Task should_record_succeeded_state_when_probe_succeeds()
    {
        var probe = new RecordingProbe(SqlServerCommitDiagnosticProbeResult.Success("ok"));
        var state = new SqlServerCommitDiagnosticProbeState();
        await using var service = _CreateService(probe, state, new SqlServerCommitCoordinationOptions());

        await service.StartAsync(TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        probe.Calls.Should().Be(1);
        state.Status.Should().Be(SqlServerCommitDiagnosticProbeStatus.Succeeded);
        state.Message.Should().Be("ok");
    }

    private static SqlServerCommitDiagnosticHostedService _CreateService(
        RecordingProbe probe,
        SqlServerCommitDiagnosticProbeState state,
        SqlServerCommitCoordinationOptions options
    )
    {
        var source = new SqlServerCommitSignalSource(
            new CommitScopeFactory(new CommitScopeStack()),
            NullLogger<SqlServerCommitSignalSource>.Instance
        );
        var observer = new SqlServerCommitDiagnosticObserver(
            source,
            NullLogger<SqlServerCommitDiagnosticObserver>.Instance
        );

        using var listenerObserver = new SqlServerCommitDiagnosticListenerObserver(observer);

        return new SqlServerCommitDiagnosticHostedService(
            listenerObserver,
            observer,
            probe,
            state,
            Options.Create(options),
            NullLogger<SqlServerCommitDiagnosticHostedService>.Instance
        );
    }

    private sealed class RecordingProbe(SqlServerCommitDiagnosticProbeResult result) : ISqlServerCommitDiagnosticProbe
    {
        public int Calls { get; private set; }

        public ValueTask<SqlServerCommitDiagnosticProbeResult> ProbeAsync(CancellationToken cancellationToken)
        {
            Calls++;

            return ValueTask.FromResult(result);
        }
    }
}
