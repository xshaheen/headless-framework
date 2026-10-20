// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Tests.Hosting;

namespace Tests.Provisioning;

[Collection(HostConsoleCollection.Name)]
public sealed class RunHeadlessDeployTests : TestBase
{
    private readonly List<string> _log = [];

    [Fact]
    public async Task should_provision_every_resource_then_run_every_seeder()
    {
        // given: a fresh database, which the starting host's own check would reject
        var schema = new RecordingProvisioner("Schema", "orders", _log)
        {
            StartupCheck = ResourceStartupCheck.FailStart,
        };
        schema.Failing.Add("verify");

        // when
        var run = await _RunAsync(
            services =>
            {
                services.AddSeeder(_ => new LoggingSeeder(_log));
                services.AddResourceProvisioner(_ => schema);
            },
            "deploy"
        );

        // then
        run.ExitCode.Should().Be(0);
        _log.Should().Equal("provision orders", "seed");
        var lines = run.StandardOutputLines();
        lines[0].Should().MatchRegex(@"^Schema +orders +Ok +\d+ms$");
        lines[1].Should().MatchRegex(@"^LoggingSeeder +Ok +\d+ms$");
    }

    [Fact]
    public async Task should_exit_1_and_run_no_seeder_when_provisioning_fails()
    {
        // given
        var queue = new RecordingProvisioner("RabbitMQ", "orders", _log) { FailureMessage = "ACCESS_REFUSED" };
        queue.Failing.Add("provision");

        // when
        var run = await _RunAsync(
            services =>
            {
                services.AddResourceProvisioner(_ => queue);
                services.AddSeeder(_ => new LoggingSeeder(_log));
            },
            "deploy"
        );

        // then
        run.ExitCode.Should().Be(1);
        run.StandardOutput.Should().Contain("ACCESS_REFUSED");
        _log.Should().Equal("provision orders");
    }

    [Theory]
    [InlineData("deploy", "--force")]
    [InlineData("deploy", "provision")]
    public async Task should_exit_2_with_the_usage_and_touch_nothing_when_given_arguments(params string[] args)
    {
        // when
        var run = await _RunAsync(
            services =>
            {
                services.AddResourceProvisioner(_ => new RecordingProvisioner("Schema", "orders", _log));
                services.AddSeeder(_ => new LoggingSeeder(_log));
            },
            args
        );

        // then
        run.ExitCode.Should().Be(2);
        run.StandardError.Should().Contain("Usage: deploy");
        _log.Should().BeEmpty();
    }

    [Fact]
    public async Task should_print_the_usage_to_stdout_on_help()
    {
        var run = await _RunAsync(_ => { }, "deploy", "--help");

        run.ExitCode.Should().Be(0);
        run.StandardOutput.Should().StartWith("Usage: deploy");
    }

    private Task<HostConsoleRun> _RunAsync(Action<IServiceCollection> configure, params string[] args)
    {
        return HostConsoleRun.RunAsync(builder => configure(builder.Services), args, AbortToken);
    }

    private sealed class LoggingSeeder(List<string> log) : ISeeder
    {
        public Task SeedAsync(CancellationToken cancellationToken)
        {
            log.Add("seed");

            return Task.CompletedTask;
        }
    }
}
