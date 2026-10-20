// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tests.Hosting;

namespace Tests.Provisioning;

[Collection(HostConsoleCollection.Name)]
public sealed class RunHeadlessResourcesTests : TestBase
{
    private readonly List<string> _log = [];

    [Fact]
    public async Task should_provision_every_resource_and_print_one_aligned_line_each()
    {
        // when
        var run = await _RunAsync(
            services =>
            {
                services.AddResourceProvisioner(_ => new RecordingProvisioner(
                    "Schema",
                    "PostgreSql/headless/Sequences",
                    _log
                ));
                services.AddResourceProvisioner(_ => new RecordingProvisioner("RabbitMQ", "orders", _log));
            },
            "resources",
            "provision"
        );

        // then
        run.ExitCode.Should().Be(0);
        _log.Should().Equal("provision PostgreSql/headless/Sequences", "provision orders");
        var lines = run.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(2);
        lines[0].Should().MatchRegex(@"^Schema    PostgreSql/headless/Sequences  Ok  +\d+ms$");
        lines[1].Should().MatchRegex(@"^RabbitMQ  orders                         Ok  +\d+ms$");
    }

    [Fact]
    public async Task should_exit_1_and_print_the_reason_on_the_resource_line_when_a_check_fails()
    {
        // given
        var queue = new RecordingProvisioner("RabbitMQ", "orders", _log)
        {
            FailureMessage = "Queue 'orders' was not found.",
        };
        queue.Failing.Add("verify");

        // when
        var run = await _RunAsync(services => services.AddResourceProvisioner(_ => queue), "resources", "verify");

        // then
        run.ExitCode.Should().Be(1);
        run.StandardOutput.Should().MatchRegex(@"RabbitMQ  orders  Failed  +\d+ms  Queue 'orders' was not found\.\n$");
    }

    [Fact]
    public async Task should_exit_0_and_mark_a_resource_unsupported_without_calling_it_when_it_cannot_purge()
    {
        // given
        var schema = new RecordingProvisioner("Schema", "orders", _log);
        schema.Unsupported.Add("purge");

        // when
        var run = await _RunAsync(services => services.AddResourceProvisioner(_ => schema), "resources", "purge");

        // then
        run.ExitCode.Should().Be(0);
        run.StandardOutput.Should().Contain("Unsupported").And.Contain("Not supported by this resource.");
        _log.Should().BeEmpty();
    }

    [Fact]
    public async Task should_delete_in_reverse_order()
    {
        // when
        var run = await _RunAsync(
            services =>
            {
                services.AddResourceProvisioner(_ => new RecordingProvisioner("Schema", "db", _log));
                services.AddResourceProvisioner(_ => new RecordingProvisioner("RabbitMQ", "orders", _log));
            },
            "resources",
            "delete"
        );

        // then
        run.ExitCode.Should().Be(0);
        _log.Should().Equal("delete orders", "delete db");
    }

    [Fact]
    public async Task should_run_only_resources_matching_every_filter_ignoring_case()
    {
        // when
        var run = await _RunAsync(
            services =>
            {
                services.AddResourceProvisioner(_ => new RecordingProvisioner("RabbitMQ", "orders", _log));
                services.AddResourceProvisioner(_ => new RecordingProvisioner("RabbitMQ", "billing", _log));
                services.AddResourceProvisioner(_ => new RecordingProvisioner("Schema", "orders", _log));
            },
            "resources",
            "provision",
            "--kind",
            "rabbitmq",
            "-n",
            "orders",
            "-n",
            "missing"
        );

        // then
        run.ExitCode.Should().Be(0);
        _log.Should().Equal("provision orders");
        run.StandardOutput.Should().StartWith("RabbitMQ  orders");
    }

    [Fact]
    public async Task should_exit_2_when_the_filters_match_no_resource()
    {
        // when
        var run = await _RunAsync(
            services => services.AddResourceProvisioner(_ => new RecordingProvisioner("Schema", "orders", _log)),
            "resources",
            "provision",
            "-k",
            "Kafka"
        );

        // then
        run.ExitCode.Should().Be(2);
        run.StandardOutput.Should().BeEmpty();
        run.StandardError.Should().Contain("No resource provisioner matches");
        _log.Should().BeEmpty();
    }

    [Fact]
    public async Task should_exit_0_and_say_so_on_stderr_when_no_resource_is_registered()
    {
        var run = await _RunAsync(_ => { }, "resources", "verify");

        run.ExitCode.Should().Be(0);
        run.StandardOutput.Should().BeEmpty();
        run.StandardError.Should().Contain("No resource provisioners are registered.");
    }

    [Theory]
    [InlineData("migrate")]
    [InlineData("provision", "--dry-run")]
    [InlineData("provision", "--kind")]
    [InlineData("provision", "--timeout", "0")]
    [InlineData("provision", "--timeout", "soon")]
    public async Task should_exit_2_with_the_usage_and_touch_no_resource_when_the_arguments_are_invalid(
        params string[] arguments
    )
    {
        // when
        var run = await _RunAsync(
            services => services.AddResourceProvisioner(_ => new RecordingProvisioner("Schema", "orders", _log)),
            ["resources", .. arguments]
        );

        // then
        run.ExitCode.Should().Be(2);
        run.StandardOutput.Should().BeEmpty();
        run.StandardError.Should().Contain("Usage: resources");
        _log.Should().BeEmpty();
    }

    [Fact]
    public async Task should_exit_2_with_the_usage_and_touch_no_resource_when_no_subcommand_is_given()
    {
        // when
        var run = await _RunAsync(
            services => services.AddResourceProvisioner(_ => new RecordingProvisioner("Schema", "orders", _log)),
            "resources"
        );

        // then
        run.ExitCode.Should().Be(2);
        run.StandardOutput.Should().BeEmpty();
        run.StandardError.Should().Contain("A subcommand is required.").And.Contain("Usage: resources");
        _log.Should().BeEmpty();
    }

    [Fact]
    public async Task should_note_in_the_list_a_resource_that_cannot_purge_or_delete()
    {
        // given
        var schema = new RecordingProvisioner("Schema", "orders", _log);
        schema.Unsupported.Add("purge");
        schema.Unsupported.Add("delete");

        // when
        var run = await _RunAsync(services => services.AddResourceProvisioner(_ => schema), "resources", "list");

        // then
        run.ExitCode.Should().Be(0);
        run.StandardOutput.Should().Contain("Cannot purge or delete.");
        _log.Should().BeEmpty();
    }

    [Fact]
    public async Task should_print_the_usage_to_stdout_on_help()
    {
        var run = await _RunAsync(_ => { }, "resources", "--help");

        run.ExitCode.Should().Be(0);
        run.StandardOutput.Should().StartWith("Usage: resources");
    }

    [Fact]
    public async Task should_list_resources_without_running_any_operation()
    {
        // when
        var run = await _RunAsync(
            services =>
            {
                services.AddResourceProvisioner(_ => new RecordingProvisioner("Schema", "orders", _log));
                services.AddResourceProvisioner(_ => new RecordingProvisioner("RabbitMQ", "orders.queue", _log));
            },
            "resources",
            "list"
        );

        // then
        run.ExitCode.Should().Be(0);
        run.StandardOutput.Should().Be("Schema    orders\nRabbitMQ  orders.queue\n");
        _log.Should().BeEmpty();
    }

    [Fact]
    public async Task should_print_each_status_value_indented_under_its_resource()
    {
        // when
        var run = await _RunAsync(
            services =>
                services.AddResourceProvisioner(_ => new RecordingProvisioner("RabbitMQ", "orders", _log)
                {
                    Status = [new("messages", "12"), new("consumers", "multi\nline")],
                }),
            "resources",
            "status"
        );

        // then
        run.ExitCode.Should().Be(0);
        run.StandardOutput.Should().EndWith("\n    messages: 12\n    consumers: multi line\n");
    }

    [Fact]
    public async Task should_report_a_timeout_when_a_driver_answers_the_elapsed_budget_with_its_own_exception()
    {
        // given
        var stalled = new RecordingProvisioner("Schema", "orders", _log);
        stalled.StallingThenDriverError.Add("provision");

        // when
        var run = await _RunAsync(
            services => services.AddResourceProvisioner(_ => stalled),
            "resources",
            "provision",
            "--timeout",
            "1"
        );

        // then
        run.ExitCode.Should().Be(1);
        run.StandardOutput.Should().Contain("TimedOut").And.NotContain("Failed");
    }

    [Fact]
    public async Task should_time_out_the_running_resource_and_skip_the_rest_when_the_budget_elapses()
    {
        // given
        var stalled = new RecordingProvisioner("RabbitMQ", "orders", _log);
        stalled.Stalling.Add("provision");

        // when
        var run = await _RunAsync(
            services =>
            {
                services.AddResourceProvisioner(_ => stalled);
                services.AddResourceProvisioner(_ => new RecordingProvisioner("Schema", "db", _log));
            },
            "resources",
            "provision",
            "--timeout",
            "1"
        );

        // then
        run.ExitCode.Should().Be(1);
        _log.Should().Equal("provision orders");
        var lines = run.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines[0].Should().Contain("TimedOut").And.Contain("while this step was running");
        lines[1].Should().Contain("TimedOut").And.Contain("Not run");
    }

    [Theory]
    [InlineData("purge", "Production")]
    [InlineData("delete", "Production")]
    [InlineData("purge", "Prod")]
    [InlineData("delete", "Staging")]
    public async Task should_refuse_a_destructive_subcommand_outside_the_allowed_environments_without_force(
        string subcommand,
        string environment
    )
    {
        // when
        var run = await _RunInAsync(
            environment,
            services => services.AddResourceProvisioner(_ => new RecordingProvisioner("Schema", "orders", _log)),
            "resources",
            subcommand
        );

        // then
        run.ExitCode.Should().Be(2);
        run.StandardError.Should()
            .Contain($"Refusing to run 'resources {subcommand}' in the '{environment}' environment");
        run.StandardError.Should().Contain("--force").And.Contain("DestructiveEnvironments");
        _log.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Test")]
    [InlineData("qa")]
    public async Task should_run_a_destructive_subcommand_without_force_in_an_allowed_environment(string environment)
    {
        // when
        var run = await _RunInAsync(
            environment,
            services =>
            {
                services.AddResourceProvisioner(_ => new RecordingProvisioner("Schema", "orders", _log));
                services.ConfigureResourceProvisioning(options => options.DestructiveEnvironments.Add("QA"));
            },
            "resources",
            "purge"
        );

        // then
        run.ExitCode.Should().Be(0);
        _log.Should().Equal("purge orders");
    }

    [Fact]
    public async Task should_run_a_destructive_subcommand_in_production_with_force()
    {
        // when
        var run = await _RunInAsync(
            Microsoft.Extensions.Hosting.Environments.Production,
            services => services.AddResourceProvisioner(_ => new RecordingProvisioner("Schema", "orders", _log)),
            "resources",
            "delete",
            "--force"
        );

        // then
        run.ExitCode.Should().Be(0);
        _log.Should().Equal("delete orders");
    }

    [Fact]
    public async Task should_run_provision_in_production_without_force()
    {
        var run = await _RunInAsync(
            Microsoft.Extensions.Hosting.Environments.Production,
            services => services.AddResourceProvisioner(_ => new RecordingProvisioner("Schema", "orders", _log)),
            "resources",
            "provision"
        );

        run.ExitCode.Should().Be(0);
        _log.Should().Equal("provision orders");
    }

    [Fact]
    public async Task should_touch_no_resource_and_exit_1_when_startup_validation_fails()
    {
        // when
        var run = await _RunAsync(
            services =>
            {
                services.AddResourceProvisioner(_ => new RecordingProvisioner("Schema", "orders", _log));
                services.AddStartupValidator(_ => new FailingValidator("The RabbitMQ password is missing."));
            },
            "resources",
            "provision"
        );

        // then
        run.ExitCode.Should().Be(1);
        run.StandardOutput.Should().BeEmpty();
        run.StandardError.Should().Contain("Resources failed: The RabbitMQ password is missing.");
        _log.Should().BeEmpty();
    }

    // Development, so purge and delete run without --force; the environment guard has tests of its own.
    private Task<HostConsoleRun> _RunAsync(Action<IServiceCollection> configure, params string[] args)
    {
        return _RunInAsync(Microsoft.Extensions.Hosting.Environments.Development, configure, args);
    }

    private Task<HostConsoleRun> _RunInAsync(
        string environment,
        Action<IServiceCollection> configure,
        params string[] args
    )
    {
        return HostConsoleRun.RunAsync(
            builder =>
            {
                builder.Environment.EnvironmentName = environment;
                configure(builder.Services);
            },
            args,
            AbortToken
        );
    }

    private sealed class FailingValidator(string message) : IHeadlessStartupValidator
    {
        public Task ValidateAsync(CancellationToken cancellationToken) =>
            Task.FromException(new InvalidOperationException(message));
    }
}
