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
    public async Task should_fail_before_any_contributor_runs_when_a_startup_validator_fails()
    {
        // given
        var contributor = new StubContributor("jobs");

        // when
        var run = await HostConsoleRun.DescribeAsync(
            builder =>
            {
                builder.Services.AddStubContributor(contributor);
                builder.Services.AddStartupValidator(_ => new FailingValidator("The cache provider is missing."));
            },
            AbortToken
        );

        // then
        run.ExitCode.Should().Be(1);
        contributor.Calls.Should().Be(0);
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
                builder.Services.AddStubContributor(new StubContributor("jobs"));
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
        var contributor = new StubContributor("jobs");

        // when
        var run = await HostConsoleRun.DescribeAsync(
            builder =>
            {
                builder.Services.AddStubContributor(contributor);
                builder
                    .Services.AddOptions<SampleOptions>()
                    .Validate(options => options.Concurrency > 0, "Concurrency must be positive.")
                    .ValidateOnStart();
            },
            AbortToken
        );

        // then
        run.ExitCode.Should().Be(1);
        contributor.Calls.Should().Be(0);
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
                builder.Services.AddStubContributor(new StubContributor("jobs"));
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
                builder.Services.AddStubContributor(new StubContributor("jobs"));
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
        var contributor = new StubContributor("jobs");

        // when
        var run = await HostConsoleRun.RunAsync(
            builder =>
            {
                builder.Services.AddHostedService(sp => worker.Bind(sp));
                builder.Services.AddStubContributor(contributor);
            },
            args,
            AbortToken
        );

        // then
        run.ExitCode.Should().Be(0);
        worker.Started.Should().BeTrue();
        contributor.Calls.Should().Be(0);
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
