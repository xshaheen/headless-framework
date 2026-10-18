// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace Tests.Hosting;

[Collection(HostConsoleCollection.Name)]
public sealed class RunHeadlessCheckTests : TestBase
{
    [Fact]
    public async Task should_print_one_line_per_check_and_exit_zero_when_every_check_is_healthy_or_degraded()
    {
        // when
        var run = await HostConsoleRun.CheckAsync(
            builder =>
                builder
                    .Services.AddHealthChecks()
                    .AddCheck("orders-db", () => HealthCheckResult.Healthy("Orders database answered."))
                    .AddCheck("search", () => HealthCheckResult.Degraded("Search index is rebuilding.")),
            AbortToken
        );

        // then
        run.ExitCode.Should().Be(0);
        var lines = run.StandardOutputLines();
        lines.Should().HaveCount(2);
        lines[0].Should().MatchRegex(@"^orders-db\s+Healthy\s+\d+ms\s+Orders database answered\.$");
        lines[1].Should().MatchRegex(@"^search\s+Degraded\s+\d+ms\s+Search index is rebuilding\.$");
    }

    [Fact]
    public async Task should_exit_one_and_print_the_probe_error_when_a_check_is_unhealthy()
    {
        // when
        var run = await HostConsoleRun.CheckAsync(
            builder =>
            {
                builder.Services.AddHealthChecks().AddCheck("orders-db", () => HealthCheckResult.Healthy());
                builder.Services.AddHeadlessHealthCheck(
                    "billing-db",
                    static (_, _) => Task.FromException(new InvalidOperationException("Connection refused.")),
                    HeadlessHealthCheckTags.Database
                );
            },
            AbortToken
        );

        // then
        run.ExitCode.Should().Be(1);
        run.StandardOutputLines()
            .Should()
            .ContainSingle(line => line.StartsWith("billing-db", StringComparison.Ordinal))
            .Which.Should()
            .MatchRegex(
                @"^billing-db\s+Unhealthy\s+\d+ms\s+The 'billing-db' dependency probe failed\. Connection refused\.$"
            );
        run.StandardOutputLines().Should().Contain(line => line.StartsWith("orders-db", StringComparison.Ordinal));
    }

    [Fact]
    public async Task should_skip_host_state_checks_without_running_them_when_checking()
    {
        // given
        var probes = 0;

        // when
        var run = await HostConsoleRun.CheckAsync(
            builder =>
                builder.Services.AddHeadlessHealthCheck(
                    "worker",
                    (_, _) =>
                    {
                        probes++;

                        return Task.FromException(new InvalidOperationException("The worker is not started."));
                    },
                    HeadlessHealthCheckTags.HostState
                ),
            AbortToken
        );

        // then
        run.ExitCode.Should().Be(0);
        probes.Should().Be(0);
        run.StandardOutputLines().Should().ContainSingle().Which.Should().MatchRegex(@"^worker\s+Skipped\s+-\s+");
    }

    [Fact]
    public async Task should_fail_before_any_check_runs_when_a_startup_validator_fails()
    {
        // given
        var probes = 0;

        // when
        var run = await HostConsoleRun.CheckAsync(
            builder =>
            {
                builder
                    .Services.AddHealthChecks()
                    .AddCheck(
                        "orders-db",
                        () =>
                        {
                            probes++;

                            return HealthCheckResult.Healthy();
                        }
                    );
                builder.Services.AddStartupValidator(_ => new FailingValidator("The cache provider is missing."));
            },
            AbortToken
        );

        // then
        run.ExitCode.Should().Be(1);
        probes.Should().Be(0);
        run.StandardOutput.Should().BeEmpty();
        run.StandardError.Should().Contain("Host check failed: The cache provider is missing.");
    }

    [Fact]
    public async Task should_never_start_a_hosted_service_while_checking()
    {
        // given
        var worker = new RecordingHostedService();

        // when
        var run = await HostConsoleRun.CheckAsync(
            builder =>
            {
                builder.Services.AddHostedService(_ => worker);
                builder.Services.AddHealthChecks().AddCheck("orders-db", () => HealthCheckResult.Healthy());
            },
            AbortToken
        );

        // then
        run.ExitCode.Should().Be(0);
        worker.Started.Should().BeFalse();
    }

    [Fact]
    public async Task should_exit_zero_and_say_so_when_no_health_check_is_registered()
    {
        // when
        var run = await HostConsoleRun.CheckAsync(_ => { }, AbortToken);

        // then
        run.ExitCode.Should().Be(0);
        run.StandardOutput.Should().BeEmpty();
        run.StandardError.Should().Contain("No health checks are registered.");
    }

    [Fact]
    public async Task should_keep_a_multiline_description_on_one_line()
    {
        // when
        var run = await HostConsoleRun.CheckAsync(
            builder =>
                builder
                    .Services.AddHealthChecks()
                    .AddCheck("orders-db", () => HealthCheckResult.Unhealthy("first line\nsecond line")),
            AbortToken
        );

        // then
        run.ExitCode.Should().Be(1);
        run.StandardOutputLines().Should().ContainSingle().Which.Should().EndWith("first line second line");
    }

    private sealed class FailingValidator(string message) : IHeadlessStartupValidator
    {
        public Task ValidateAsync(CancellationToken cancellationToken) =>
            Task.FromException(new InvalidOperationException(message));
    }

    private sealed class RecordingHostedService : IHostedService
    {
        public bool Started { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Started = true;

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
