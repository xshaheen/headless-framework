// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Logging;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Tests.Hosting;

[Collection(HostConsoleCollection.Name)]
public sealed class RunHeadlessAsyncTests : TestBase
{
    [Fact]
    public async Task should_fail_before_any_section_runs_when_a_startup_validator_fails()
    {
        // given
        var section = new StubSection("jobs");

        // when
        var run = await HostConsoleRun.DescribeAsync(
            builder =>
            {
                builder.Services.AddStubSection(section);
                builder.Services.AddStartupValidator(_ => new FailingValidator("The cache provider is missing."));
            },
            AbortToken
        );

        // then
        run.ExitCode.Should().Be(1);
        section.Calls.Should().Be(0);
        run.StandardError.Should().Contain("The cache provider is missing.");
    }

    [Fact]
    public async Task should_print_the_catalog_error_to_stderr_and_nothing_to_stdout_when_the_jobs_catalog_is_rejected()
    {
        // given
        const string catalogError =
            "Jobs catalog validation failed: function 'send-invoices' has no handler registered.";

        // when
        var run = await HostConsoleRun.DescribeAsync(
            builder =>
            {
                builder.Services.AddStubSection(new StubSection("jobs"));
                builder.Services.AddStartupValidator(_ => new FailingValidator(catalogError));
            },
            AbortToken
        );

        // then
        run.StandardOutput.Should().BeEmpty();
        run.StandardError.Should().Contain(catalogError);
        run.ExitCode.Should().Be(1);
    }

    [Fact]
    public async Task should_fail_when_an_option_validated_on_start_is_invalid()
    {
        // given
        var section = new StubSection("jobs");

        // when
        var run = await HostConsoleRun.DescribeAsync(
            builder =>
            {
                builder.Services.AddStubSection(section);
                builder
                    .Services.AddOptions<SampleOptions>()
                    .Validate(options => options.Concurrency > 0, "Concurrency must be positive.")
                    .ValidateOnStart();
            },
            AbortToken
        );

        // then
        run.ExitCode.Should().Be(1);
        section.Calls.Should().Be(0);
        run.StandardOutput.Should().BeEmpty();
        run.StandardError.Should().Contain("Concurrency must be positive.");
    }

    [Fact]
    public async Task should_keep_default_console_logging_off_stdout_while_describing()
    {
        // when
        var run = await HostConsoleRun.DescribeAsync(
            builder =>
            {
                builder.Services.AddStubSection(new StubSection("jobs"));
                builder.Services.AddStartupValidator(sp => new LoggingValidator(
                    sp.GetRequiredService<ILogger<LoggingValidator>>()
                ));
            },
            AbortToken
        );

        // then
        run.ExitCode.Should().Be(0);
        using var document = run.ParseStandardOutput();
        document.RootElement.GetProperty("schemaVersion").GetInt32().Should().Be(1);
        run.StandardError.Should().Contain(LoggingValidator.Message);
    }

    [Fact]
    public async Task should_warn_on_stderr_when_a_default_builder_host_cannot_move_console_logging_off_stdout()
    {
        // given a legacy generic host, whose console logger captured standard output when it was built
        var originalOut = Console.Out;
        var originalError = Console.Error;
        await using var stdout = new StringWriter();
        await using var stderr = new StringWriter();
        Console.SetOut(stdout);
        Console.SetError(stderr);

        int exitCode;
        try
        {
            var host = Host.CreateDefaultBuilder().Build();

            // when
            exitCode = await host.RunHeadlessAsync(["describe"], AbortToken);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }

        // then - the manifest is still written, and the operator is told why a piped parse may break
        exitCode.Should().Be(0);
        stderr.ToString().Should().Contain("cannot be moved off standard output");
        stdout.ToString().Should().Contain("\"schemaVersion\"");
    }

    [Fact]
    public async Task should_keep_headless_serilog_console_output_off_stdout_while_describing()
    {
        // when
        var run = await HostConsoleRun.DescribeAsync(
            builder =>
            {
                builder.Services.AddSerilog(
                    (sp, configuration) =>
                        configuration.ConfigureReloadableLoggerConfiguration(
                            sp,
                            builder.Configuration,
                            builder.Environment,
                            new SerilogOptions { WriteToFiles = false }
                        ),
                    preserveStaticLogger: true
                );
                builder.Services.AddStubSection(new StubSection("jobs"));
                builder.Services.AddStartupValidator(sp => new LoggingValidator(
                    sp.GetRequiredService<ILogger<LoggingValidator>>()
                ));
            },
            AbortToken
        );

        // then
        run.ExitCode.Should().Be(0);
        using var document = run.ParseStandardOutput();
        document.RootElement.GetProperty("schemaVersion").GetInt32().Should().Be(1);
        run.StandardError.Should().Contain(LoggingValidator.Message);
    }

    [Fact]
    public async Task should_never_start_a_hosted_service_while_describing()
    {
        // given
        var worker = new StoppingHostedService();

        // when
        var run = await HostConsoleRun.DescribeAsync(
            builder => builder.Services.AddHostedService(sp => worker.Bind(sp)),
            AbortToken
        );

        // then
        run.ExitCode.Should().Be(0);
        worker.Started.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("--urls http://localhost:5000")]
    [InlineData("Describe")]
    public async Task should_run_the_host_when_the_first_argument_is_not_describe(string commandLine)
    {
        // given
        var args = commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var worker = new StoppingHostedService();
        var section = new StubSection("jobs");

        // when
        var run = await HostConsoleRun.RunAsync(
            builder =>
            {
                builder.Services.AddHostedService(sp => worker.Bind(sp));
                builder.Services.AddStubSection(section);
            },
            args,
            AbortToken
        );

        // then
        run.ExitCode.Should().Be(0);
        worker.Started.Should().BeTrue();
        section.Calls.Should().Be(0);
    }

    [Fact]
    public async Task should_exit_1_when_a_background_service_fault_stops_the_host()
    {
        // when
        var run = await HostConsoleRun.RunAsync(
            builder => builder.Services.AddHostedService<FaultingBackgroundService>(),
            [],
            AbortToken
        );

        // then
        run.ExitCode.Should().Be(1, "an orchestrator must see a host that stopped on a fault as a failed process");
        run.StandardError.Should().Contain(nameof(FaultingBackgroundService)).And.Contain("queue is missing");
    }

    [Fact]
    public async Task should_exit_0_when_a_background_service_faults_only_while_the_host_shuts_down()
    {
        // given: a clean stop, during which the service throws (a disposed client, say)
        var worker = new StoppingHostedService();

        // when
        var run = await HostConsoleRun.RunAsync(
            builder =>
            {
                builder.Services.AddHostedService<FaultingOnStopBackgroundService>();
                builder.Services.AddHostedService(sp => worker.Bind(sp));
            },
            [],
            AbortToken
        );

        // then
        worker.Started.Should().BeTrue();
        run.ExitCode.Should()
            .Be(0, "a fault raised by the shutdown itself must not turn a clean rollout into a failure");
        run.StandardError.Should().NotContain("Host stopped:");
    }

    [Fact]
    public async Task should_exit_0_when_a_stop_requested_during_startup_makes_a_background_service_fault()
    {
        // given: a service that throws as soon as its stopping token is cancelled, and a stop requested while the host
        // is still starting, once that service is listening
        using var service = new FaultingOnCancellationBackgroundService();
        var stopper = new StopWhenReadyHostedService(service.Listening);

        // when
        var run = await HostConsoleRun.RunAsync(
            builder =>
            {
                builder.Services.AddHostedService(_ => service);
                builder.Services.AddHostedService(sp => stopper.Bind(sp));
            },
            [],
            AbortToken
        );

        // then
        service
            .ExecuteTask!.IsFaulted.Should()
            .BeTrue("the service must have thrown for the exit code to mean anything");
        run.ExitCode.Should().Be(0, "a fault raised by the stop itself must not turn a clean stop into a failure");
        run.StandardError.Should().NotContain("Host stopped:");
    }

    // Throws from inside the stopping-token callback chain: a stop requested while the host starts cancels that token
    // before RunHeadlessAsync's stopping callback, so the fault is already visible when the faults are counted.
    private sealed class FaultingOnCancellationBackgroundService : BackgroundService
    {
        private readonly TaskCompletionSource _listening = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Listening => _listening.Task;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Synchronous continuations: the throw below runs inside the cancellation itself.
            var cancelled = new TaskCompletionSource();

            await using (stoppingToken.Register(() => cancelled.TrySetResult()))
            {
                _listening.TrySetResult();
                await cancelled.Task;
            }

            throw new ObjectDisposedException("The client was disposed.");
        }
    }

    // Requests the stop from its StartAsync, so the host is still starting, after the faulting service is listening.
    private sealed class StopWhenReadyHostedService(Task ready) : IHostedService
    {
        private IHostApplicationLifetime? _lifetime;

        public StopWhenReadyHostedService Bind(IServiceProvider services)
        {
            _lifetime = services.GetRequiredService<IHostApplicationLifetime>();

            return this;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await ready.WaitAsync(cancellationToken);
            _lifetime!.StopApplication();
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FaultingOnStopBackgroundService : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken)
                .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

            throw new ObjectDisposedException("The client was disposed.");
        }
    }

    private sealed class FaultingBackgroundService : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Yield();

            throw new InvalidOperationException("The queue is missing. Run 'resources provision'.");
        }
    }

    private sealed class SampleOptions
    {
        public int Concurrency { get; set; }
    }

    private sealed class FailingValidator(string message) : IHeadlessStartupValidator
    {
        public Task ValidateAsync(CancellationToken cancellationToken) =>
            Task.FromException(new InvalidOperationException(message));
    }

    private sealed class LoggingValidator(ILogger<LoggingValidator> logger) : IHeadlessStartupValidator
    {
        public const string Message = "advisory finding from a passing validator";

        public Task ValidateAsync(CancellationToken cancellationToken)
        {
#pragma warning disable CA1848, CA2254 // CA2254 is a false positive (the template is a const); CA1848's LoggerMessage delegate would make this test stub worse.
            logger.LogWarning(Message);
#pragma warning restore CA1848, CA2254

            return Task.CompletedTask;
        }
    }

    private sealed class StoppingHostedService : IHostedService
    {
        private IHostApplicationLifetime? _lifetime;

        public bool Started { get; private set; }

        public StoppingHostedService Bind(IServiceProvider services)
        {
            _lifetime = services.GetRequiredService<IHostApplicationLifetime>();

            return this;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Started = true;
            _lifetime?.StopApplication();

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
