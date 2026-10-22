// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Hosting.Schema;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tests.Hosting;
using Tests.Provisioning;

namespace Tests.Seeders;

[Collection(HostConsoleCollection.Name)]
public sealed class SeedVerbTests : TestBase
{
    [Fact]
    public async Task should_run_seeders_in_dependency_order_and_print_one_line_each()
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

    [Fact]
    public async Task should_exit_0_and_print_each_state_when_every_store_is_current_or_cannot_tell()
    {
        // when
        var run = await _RunAsync(
            services =>
            {
                services.AddSeeder(_ => new VerifyingSeeder("Definitions", SeedState.Current));
                services.AddSeeder<UnknownSeeder>();
            },
            "seed",
            "verify"
        );

        // then
        run.ExitCode.Should().Be(0, "a seeder that cannot tell must not fail a deployment that seeded correctly");
        var lines = run.StandardOutputLines();
        lines.Should().HaveCount(2);
        lines[0].Should().MatchRegex(@"^Definitions +Current +\d+ms$");
        lines[1].Should().MatchRegex(@"^Unknown +Unknown +\d+ms +This seeder cannot tell\.$");
        SeedLog.Entries.Should().BeEmpty("verify never writes");
    }

    [Fact]
    public async Task should_exit_1_and_name_the_fix_when_a_store_is_stale()
    {
        // when
        var run = await _RunAsync(
            services =>
            {
                services.AddSeeder(_ => new VerifyingSeeder("Definitions", SeedState.Current));
                services.AddSeeder<StaleSeeder>();
            },
            "seed",
            "verify"
        );

        // then
        run.ExitCode.Should().Be(1);
        run.StandardOutput.Should()
            .MatchRegex(@"Jobs +Stale +\d+ms +The store does not hold what the code declares\. Run 'seed'\.");
        SeedLog.Entries.Should().BeEmpty("verify never writes");
    }

    [Fact]
    public async Task should_exit_1_and_still_verify_the_rest_when_a_check_fails()
    {
        // when
        var run = await _RunAsync(
            services =>
            {
                services.AddSeeder<BrokenVerifySeeder>();
                services.AddSeeder(_ => new VerifyingSeeder("Definitions", SeedState.Current));
            },
            "seed",
            "verify"
        );

        // then
        run.ExitCode.Should().Be(1);
        run.StandardOutput.Should().MatchRegex(@"Broken +Failed +\d+ms +The definitions table is unreachable\.");
        run.StandardOutput.Should().MatchRegex(@"Definitions +Current +\d+ms");
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
    public async Task should_seed_after_resource_provisioning_and_before_hosted_services_start()
    {
        // given
        var log = new List<string>();
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddHostedService(_ => new StartLogger(log));
        builder.Services.AddSingleton(log);
        builder.Services.AddSeeder<ListSeeder>();
        builder.Services.AddResourceProvisioner(_ => new RecordingProvisioner("Schema", "definitions", log));
        builder.Services.AddDeployStepsOnStartup();
        using var host = builder.Build();

        // when
        await host.StartAsync(AbortToken);
        await host.Services.WaitForWarmupAsync(AbortToken);
        await host.StopAsync(AbortToken);

        // then: the readiness check verifies the resource only once the host has started
        log.Should().Equal("provision definitions", "seed", "start", "verify definitions");
    }

    [Fact]
    public async Task should_fail_the_start_naming_the_seeder_and_start_no_later_service_when_a_seeder_fails()
    {
        // given
        var log = new List<string>();
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSeeder<FailingSeeder>();
        builder.Services.AddDeployStepsOnStartup(o => o.Resources = ResourceStartupAction.None);
        builder.Services.AddHostedService(_ => new StartLogger(log));
        using var host = builder.Build();

        // when
        var act = () => host.StartAsync(AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Seeder Failing failed*locked*");
        log.Should().BeEmpty("a hosted service registered after the seeding must not start on unseeded data");
    }

    [Fact]
    public async Task should_start_without_seeding_when_the_host_does_not_opt_in()
    {
        // given
        SeedLog.Entries.Clear();
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSeeder<FirstSeeder>();
        using var host = builder.Build();

        // when
        await host.StartAsync(AbortToken);
        await host.StopAsync(AbortToken);

        // then
        SeedLog.Entries.Should().BeEmpty("the deployment's seed verb is the default runner");
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
    public async Task should_verify_the_schema_before_seeding_so_a_missing_table_names_resources_provision()
    {
        // given — a host that seeds on startup but never provisioned its tables
        using var db = new Tests.Initialization.SqliteSchemaDatabase();
        var log = new List<string>();
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton(log);
        builder.Services.AddSeeder<ListSeeder>();
        builder.Services.AddDeployStepsOnStartup(o => o.Resources = ResourceStartupAction.None);
        var contribution = db.Contribution(
            "Widgets",
            "s",
            new Headless.Hosting.Schema.SchemaStep("1", "w", "CREATE TABLE IF NOT EXISTS s_w (id INTEGER)")
        );
        builder.Services.AddHeadlessSchemaContribution(_ => contribution);
        using var host = builder.Build();

        // when
        var act = () => host.StartAsync(AbortToken);

        // then
        await act.Should().ThrowAsync<ResourceProvisioningException>().WithMessage("*'resources provision'*");
        log.Should().BeEmpty("no seeder runs against a schema that is not set up");
    }

    private sealed class SameNameSeeder : ISeeder
    {
        public string Name => "First";

        public Task SeedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
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
        public Task SeedAsync(CancellationToken cancellationToken)
        {
            log.Add("seed");

            return Task.CompletedTask;
        }
    }
}

internal static class SeedLog
{
    public static List<string> Entries { get; } = [];
}

internal sealed class FirstSeeder : ISeeder
{
    public string Name => "First";

    public Task SeedAsync(CancellationToken cancellationToken)
    {
        SeedLog.Entries.Add("first");

        return Task.CompletedTask;
    }
}

internal sealed class SecondSeeder : ISeeder
{
    public string Name => "Second";

    // Registered before both, so only this puts it after them.
    public IEnumerable<ISeeder> DependsOn(IReadOnlyList<ISeeder> seeders) =>
        seeders.Where(static s => s is FirstSeeder or FailingSeeder);

    public Task SeedAsync(CancellationToken cancellationToken)
    {
        SeedLog.Entries.Add("second");

        return Task.CompletedTask;
    }
}

internal sealed class FailingSeeder : ISeeder
{
    public string Name => "Failing";

    public Task SeedAsync(CancellationToken cancellationToken) =>
        throw new InvalidOperationException("The definitions table is locked.");
}

internal sealed class VerifyingSeeder(string name, SeedState state) : ISeeder
{
    public string Name => name;

    public Task SeedAsync(CancellationToken cancellationToken)
    {
        SeedLog.Entries.Add(name);

        return Task.CompletedTask;
    }

    public Task<SeedState> VerifyAsync(CancellationToken cancellationToken) => Task.FromResult(state);
}

internal sealed class StaleSeeder : ISeeder
{
    public string Name => "Jobs";

    public Task SeedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<SeedState> VerifyAsync(CancellationToken cancellationToken) => Task.FromResult(SeedState.Stale);
}

internal sealed class UnknownSeeder : ISeeder
{
    public string Name => "Unknown";

    public Task SeedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class BrokenVerifySeeder : ISeeder
{
    public string Name => "Broken";

    public Task SeedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<SeedState> VerifyAsync(CancellationToken cancellationToken) =>
        Task.FromException<SeedState>(new InvalidOperationException("The definitions table is unreachable."));
}
