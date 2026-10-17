// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tests.Hosting;
using Tests.StatefulResources;

namespace Tests.Seeders;

[Collection(HostConsoleCollection.Name)]
public sealed class SeedVerbTests : TestBase
{
    [Fact]
    public async Task should_run_seeders_in_priority_order_and_print_one_line_each()
    {
        // when
        var run = await _RunAsync(services => _AddSeeders(services), "seed");

        // then
        run.ExitCode.Should().Be(0);
        SeedLog.Entries.Should().Equal("first", "second");
        var lines = run.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines[0].Should().MatchRegex(@"^First   Ok  +\d+ms$");
        lines[1].Should().MatchRegex(@"^Second  Ok  +\d+ms$");
    }

    [Fact]
    public async Task should_stop_at_the_first_failure_and_skip_the_rest_with_exit_1()
    {
        // when
        var run = await _RunAsync(
            services =>
            {
                _AddSeeders(services);
                services.AddSeeder<FailingSeeder>();
            },
            "seed"
        );

        // then
        run.ExitCode.Should().Be(1);
        run.StandardOutput.Should().Contain("Failing  Failed").And.Contain("definitions table is locked");
        run.StandardOutput.Should().MatchRegex(@"Second +Skipped .*Not run: an earlier seeder failed\.");
    }

    [Fact]
    public async Task should_run_only_the_named_seeder_and_list_without_running()
    {
        var named = await _RunAsync(services => _AddSeeders(services), "seed", "-n", "second");
        named.ExitCode.Should().Be(0);
        SeedLog.Entries.Should().Equal("second");

        SeedLog.Entries.Clear();
        var listed = await _RunAsync(services => _AddSeeders(services), "seed", "list");
        listed.StandardOutput.Should().Be("First\nSecond\n");
        SeedLog.Entries.Should().BeEmpty();
    }

    [Theory]
    [InlineData("seed", "-n", "missing")]
    [InlineData("seed", "run")]
    [InlineData("seed", "--timeout", "0")]
    public async Task should_exit_2_on_invalid_arguments(params string[] args)
    {
        var run = await _RunAsync(services => _AddSeeders(services), args);

        run.ExitCode.Should().Be(2);
        SeedLog.Entries.Should().BeEmpty();
    }

    private Task<HostConsoleRun> _RunAsync(Action<IServiceCollection> configure, params string[] args)
    {
        SeedLog.Entries.Clear();

        return HostConsoleRun.RunAsync(builder => configure(builder.Services), args, AbortToken);
    }

    internal static void _AddSeeders(IServiceCollection services)
    {
        services.AddSeeder<SecondSeeder>();
        services.AddSeeder<FirstSeeder>();
    }
}

public sealed class SeedOnStartupTests : TestBase
{
    [Fact]
    public async Task should_seed_after_resource_setup_and_before_hosted_services_start()
    {
        // given
        var log = new List<string>();
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddHostedService(_ => new StartLogger(log));
        builder.Services.AddSingleton(log);
        builder.Services.AddSeeder<ListSeeder>();
        builder.Services.AddHeadlessSeedOnStartup();
        builder.Services.AddHeadlessStatefulResource(_ => new RecordingResource("Schema", "definitions", log));
        builder.Services.AddHeadlessResourceSetupOnStartup();
        using var host = builder.Build();

        // when
        await host.StartAsync(AbortToken);
        await host.StopAsync(AbortToken);

        // then
        log.Should().Equal("setup definitions", "seed", "start");
    }

    [Fact]
    public async Task should_fail_the_start_naming_the_seeder_and_start_no_later_service_when_a_seeder_fails()
    {
        // given
        var log = new List<string>();
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSeeder<FailingSeeder>();
        builder.Services.AddHeadlessSeedOnStartup();
        builder.Services.AddHostedService(_ => new StartLogger(log));
        using var host = builder.Build();

        // when
        var act = () => host.StartAsync(AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Seeder Failing failed*locked*");
        log.Should().BeEmpty("a hosted service registered after the seeding must not start on unseeded data");
    }

    [Fact]
    public async Task should_warn_once_when_seeders_are_registered_and_nothing_runs_them()
    {
        // given
        using var logs = new CapturingLoggerProvider();
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.AddProvider(logs);
        builder.Services.AddSeeder<FirstSeeder>();
        using var host = builder.Build();

        // when
        await host.StartAsync(AbortToken);
        await host.StopAsync(AbortToken);

        // then
        logs.Warnings.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("'seed' verb")
            .And.Contain("AddHeadlessSeedOnStartup()");
    }

    [Fact]
    public async Task should_not_warn_when_the_deployment_seeds()
    {
        using var logs = new CapturingLoggerProvider();
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.AddProvider(logs);
        builder.Services.AddSeeder<FirstSeeder>();
        builder.Services.AddHeadlessSeedDuringDeploy();
        using var host = builder.Build();

        await host.StartAsync(AbortToken);
        await host.StopAsync(AbortToken);

        logs.Warnings.Should().BeEmpty();
        SeedLog.Entries.Should().NotContain("first", "declaring the deploy step runs nothing at startup");
    }

    [Fact]
    public void should_register_a_seeder_factory_once_and_refuse_a_different_one_for_the_same_type()
    {
        // given
        var services = new ServiceCollection();
        Func<IServiceProvider, FirstSeeder> factory = static _ => new FirstSeeder();

        // when
        services.AddSeeder(factory);
        services.AddSeeder(factory);
        var other = () => services.AddSeeder(static _ => new FirstSeeder());

        // then — the same factory again is a repeated setup; another one could never both run
        services.Count(d => d.ServiceType == typeof(ISeeder)).Should().Be(1);
        other.Should().Throw<InvalidOperationException>().WithMessage("*FirstSeeder is already registered*");
    }

    [Fact]
    public async Task should_refuse_to_seed_when_two_seeders_share_a_name()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSeeder<FirstSeeder>();
        builder.Services.AddSeeder<SameNameSeeder>();
        await using var provider = builder.Services.BuildServiceProvider();

        var act = () => provider.SeedAsync(cancellationToken: AbortToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*share the name 'First'*");
    }

    [Fact]
    public async Task should_verify_the_schema_before_seeding_so_a_missing_table_names_resources_setup()
    {
        // given — a host that seeds on startup but never set its tables up
        using var db = new Tests.Initialization.SqliteSchemaDatabase();
        var log = new List<string>();
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton(log);
        builder.Services.AddSeeder<ListSeeder>();
        builder.Services.AddHeadlessSeedOnStartup();
        var contribution = db.Contribution(
            "Widgets",
            "s",
            new Headless.Hosting.Initialization.Schema.SchemaStep(
                "1",
                "w",
                "CREATE TABLE IF NOT EXISTS s_w (id INTEGER)"
            )
        );
        builder.Services.AddHeadlessSchemaContribution(_ => contribution);
        using var host = builder.Build();

        // when
        var act = () => host.StartAsync(AbortToken);

        // then
        await act.Should()
            .ThrowAsync<Headless.Hosting.Initialization.Schema.SchemaRunnerException>()
            .WithMessage("*'resources setup'*");
        log.Should().BeEmpty("no seeder runs against a schema that is not set up");
    }

    private sealed class SameNameSeeder : ISeeder
    {
        public string Name => "First";

        public ValueTask SeedAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class StartLogger(List<string> log) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            log.Add("start");

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class ListSeeder(List<string> log) : ISeeder
    {
        public ValueTask SeedAsync(CancellationToken cancellationToken = default)
        {
            log.Add("seed");

            return ValueTask.CompletedTask;
        }
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public List<string> Warnings { get; } = [];

        public ILogger CreateLogger(string categoryName) => new Logger(this);

        public void Dispose() { }

        private sealed class Logger(CapturingLoggerProvider owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter
            )
            {
                if (
                    logLevel == LogLevel.Warning
                    && string.Equals(eventId.Name, "SeedingNotConfigured", StringComparison.Ordinal)
                )
                {
                    lock (owner.Warnings)
                    {
                        owner.Warnings.Add(formatter(state, exception));
                    }
                }
            }
        }
    }
}

internal static class SeedLog
{
    public static List<string> Entries { get; } = [];
}

[SeederPriority(1)]
internal sealed class FirstSeeder : ISeeder
{
    public string Name => "First";

    public ValueTask SeedAsync(CancellationToken cancellationToken = default)
    {
        SeedLog.Entries.Add("first");

        return ValueTask.CompletedTask;
    }
}

[SeederPriority(3)]
internal sealed class SecondSeeder : ISeeder
{
    public string Name => "Second";

    public ValueTask SeedAsync(CancellationToken cancellationToken = default)
    {
        SeedLog.Entries.Add("second");

        return ValueTask.CompletedTask;
    }
}

[SeederPriority(2)]
internal sealed class FailingSeeder : ISeeder
{
    public string Name => "Failing";

    public ValueTask SeedAsync(CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("The definitions table is locked.");
}
