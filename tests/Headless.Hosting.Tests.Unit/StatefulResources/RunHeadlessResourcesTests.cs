// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Tests.Hosting;

namespace Tests.StatefulResources;

[Collection(HostConsoleCollection.Name)]
public sealed class RunHeadlessResourcesTests : TestBase
{
    private readonly List<string> _log = [];

    [Fact]
    public async Task should_set_up_every_resource_and_print_one_aligned_line_each_when_no_subcommand_is_given()
    {
        // when
        var run = await _RunAsync(
            services =>
            {
                services.AddHeadlessStatefulResource(_ => new RecordingResource(
                    "Schema",
                    "PostgreSql/headless/Sequences",
                    _log
                ));
                services.AddHeadlessStatefulResource(_ => new RecordingResource("RabbitMQ", "orders", _log));
            },
            "resources"
        );

        // then
        run.ExitCode.Should().Be(0);
        _log.Should().Equal("setup PostgreSql/headless/Sequences", "setup orders");
        var lines = run.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(2);
        lines[0].Should().MatchRegex(@"^Schema    PostgreSql/headless/Sequences  Ok  +\d+ms$");
        lines[1].Should().MatchRegex(@"^RabbitMQ  orders                         Ok  +\d+ms$");
    }

    [Fact]
    public async Task should_exit_1_and_print_the_reason_on_the_resource_line_when_a_check_fails()
    {
        // given
        var queue = new RecordingResource("RabbitMQ", "orders", _log)
        {
            FailureMessage = "Queue 'orders' was not found.",
        };
        queue.Failing.Add("check");

        // when
        var run = await _RunAsync(services => services.AddHeadlessStatefulResource(_ => queue), "resources", "check");

        // then
        run.ExitCode.Should().Be(1);
        run.StandardOutput.Should().MatchRegex(@"RabbitMQ  orders  Failed  +\d+ms  Queue 'orders' was not found\.\n$");
    }

    [Fact]
    public async Task should_exit_0_and_mark_a_resource_unsupported_when_it_cannot_clear()
    {
        // given
        var schema = new RecordingResource("Schema", "orders", _log);
        schema.Unsupported.Add("clear");

        // when
        var run = await _RunAsync(services => services.AddHeadlessStatefulResource(_ => schema), "resources", "clear");

        // then
        run.ExitCode.Should().Be(0);
        run.StandardOutput.Should().Contain("Unsupported").And.Contain("Not supported by this resource.");
    }

    [Fact]
    public async Task should_tear_down_in_reverse_order()
    {
        // when
        var run = await _RunAsync(
            services =>
            {
                services.AddHeadlessStatefulResource(_ => new RecordingResource("Schema", "db", _log));
                services.AddHeadlessStatefulResource(_ => new RecordingResource("RabbitMQ", "orders", _log));
            },
            "resources",
            "teardown"
        );

        // then
        run.ExitCode.Should().Be(0);
        _log.Should().Equal("teardown orders", "teardown db");
    }

    [Fact]
    public async Task should_run_only_resources_matching_every_filter_ignoring_case()
    {
        // when
        var run = await _RunAsync(
            services =>
            {
                services.AddHeadlessStatefulResource(_ => new RecordingResource("RabbitMQ", "orders", _log));
                services.AddHeadlessStatefulResource(_ => new RecordingResource("RabbitMQ", "billing", _log));
                services.AddHeadlessStatefulResource(_ => new RecordingResource("Schema", "orders", _log));
            },
            "resources",
            "setup",
            "--type",
            "rabbitmq",
            "-n",
            "orders",
            "-n",
            "missing"
        );

        // then
        run.ExitCode.Should().Be(0);
        _log.Should().Equal("setup orders");
        run.StandardOutput.Should().StartWith("RabbitMQ  orders");
    }

    [Fact]
    public async Task should_exit_2_when_the_filters_match_no_resource()
    {
        // when
        var run = await _RunAsync(
            services => services.AddHeadlessStatefulResource(_ => new RecordingResource("Schema", "orders", _log)),
            "resources",
            "-t",
            "Kafka"
        );

        // then
        run.ExitCode.Should().Be(2);
        run.StandardOutput.Should().BeEmpty();
        run.StandardError.Should().Contain("No stateful resource matches");
        _log.Should().BeEmpty();
    }

    [Fact]
    public async Task should_exit_0_and_say_so_on_stderr_when_no_resource_is_registered()
    {
        var run = await _RunAsync(_ => { }, "resources", "check");

        run.ExitCode.Should().Be(0);
        run.StandardOutput.Should().BeEmpty();
        run.StandardError.Should().Contain("No stateful resources are registered.");
    }

    [Theory]
    [InlineData("migrate")]
    [InlineData("setup", "--force")]
    [InlineData("setup", "--type")]
    [InlineData("setup", "--timeout", "0")]
    [InlineData("setup", "--timeout", "soon")]
    public async Task should_exit_2_with_the_usage_and_touch_no_resource_when_the_arguments_are_invalid(
        params string[] arguments
    )
    {
        // when
        var run = await _RunAsync(
            services => services.AddHeadlessStatefulResource(_ => new RecordingResource("Schema", "orders", _log)),
            ["resources", .. arguments]
        );

        // then
        run.ExitCode.Should().Be(2);
        run.StandardOutput.Should().BeEmpty();
        run.StandardError.Should().Contain("Usage: resources");
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
                services.AddHeadlessStatefulResource(_ => new RecordingResource("Schema", "orders", _log));
                services.AddHeadlessStatefulResource(_ => new RecordingResource("RabbitMQ", "orders.queue", _log));
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
                services.AddHeadlessStatefulResource(_ => new RecordingResource("RabbitMQ", "orders", _log)
                {
                    Status = [new("messages", "12"), new("consumers", "multi\nline")],
                }),
            "resources",
            "statistics"
        );

        // then
        run.ExitCode.Should().Be(0);
        run.StandardOutput.Should().EndWith("\n    messages: 12\n    consumers: multi line\n");
    }

    [Fact]
    public async Task should_time_out_the_running_resource_and_skip_the_rest_when_the_budget_elapses()
    {
        // given
        var stalled = new RecordingResource("RabbitMQ", "orders", _log);
        stalled.Stalling.Add("setup");

        // when
        var run = await _RunAsync(
            services =>
            {
                services.AddHeadlessStatefulResource(_ => stalled);
                services.AddHeadlessStatefulResource(_ => new RecordingResource("Schema", "db", _log));
            },
            "resources",
            "--timeout",
            "1"
        );

        // then
        run.ExitCode.Should().Be(1);
        _log.Should().Equal("setup orders");
        var lines = run.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines[0].Should().Contain("TimedOut").And.Contain("while this step was running");
        lines[1].Should().Contain("TimedOut").And.Contain("Not run");
    }

    [Fact]
    public async Task should_touch_no_resource_and_exit_1_when_startup_validation_fails()
    {
        // when
        var run = await _RunAsync(
            services =>
            {
                services.AddHeadlessStatefulResource(_ => new RecordingResource("Schema", "orders", _log));
                services.AddStartupValidator(_ => new FailingValidator("The RabbitMQ password is missing."));
            },
            "resources"
        );

        // then
        run.ExitCode.Should().Be(1);
        run.StandardOutput.Should().BeEmpty();
        run.StandardError.Should().Contain("Resources failed: The RabbitMQ password is missing.");
        _log.Should().BeEmpty();
    }

    private Task<HostConsoleRun> _RunAsync(Action<IServiceCollection> configure, params string[] args)
    {
        return HostConsoleRun.RunAsync(builder => configure(builder.Services), args, AbortToken);
    }

    private sealed class FailingValidator(string message) : IHeadlessStartupValidator
    {
        public Task ValidateAsync(CancellationToken cancellationToken) =>
            Task.FromException(new InvalidOperationException(message));
    }
}
