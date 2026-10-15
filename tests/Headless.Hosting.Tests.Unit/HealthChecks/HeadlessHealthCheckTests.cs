// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Testing.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests.HealthChecks;

public sealed class HeadlessHealthCheckTests : TestBase
{
    [Fact]
    public void should_register_check_with_readiness_and_headless_tags_plus_given_tags()
    {
        // given
        var services = _CreateServices();

        // when
        services.AddHeadlessHealthCheck("db", (_, _) => Task.CompletedTask, HeadlessHealthCheckTags.Database);
        using var provider = services.BuildServiceProvider();

        // then
        var registration = _Registrations(provider).Should().ContainSingle().Subject;
        registration.Name.Should().Be("db");
        registration.FailureStatus.Should().Be(HealthStatus.Unhealthy);
        registration
            .Tags.Should()
            .BeEquivalentTo(
                HeadlessHealthCheckTags.Ready,
                HeadlessHealthCheckTags.Headless,
                HeadlessHealthCheckTags.Database
            );
    }

    [Fact]
    public void should_keep_one_check_when_the_same_name_is_contributed_twice()
    {
        // given
        var services = _CreateServices();

        // when
        services.AddHeadlessHealthCheck("db", (_, _) => Task.CompletedTask);
        services.AddHeadlessHealthCheck("db", (_, _) => Task.CompletedTask);
        using var provider = services.BuildServiceProvider();

        // then
        _Registrations(provider).Should().ContainSingle();
    }

    [Fact]
    public void should_add_the_second_contribution_tags_to_the_one_check()
    {
        // given
        var services = _CreateServices();

        // when - two packages probe one dependency under one name
        services.AddHeadlessHealthCheck("db", (_, _) => Task.CompletedTask, HeadlessHealthCheckTags.Database);
        services.AddHeadlessHealthCheck("db", (_, _) => Task.CompletedTask, HeadlessHealthCheckTags.Jobs);
        using var provider = services.BuildServiceProvider();

        // then
        _Registrations(provider)
            .Should()
            .ContainSingle()
            .Which.Tags.Should()
            .Contain([HeadlessHealthCheckTags.Database, HeadlessHealthCheckTags.Jobs]);
    }

    [Fact]
    public void should_drop_a_check_that_configuration_turns_off()
    {
        // given
        var services = _CreateServices(("Headless:HealthChecks:db:Enabled", "false"));

        // when
        services.AddHeadlessHealthCheck("db", (_, _) => Task.CompletedTask);
        services.AddHeadlessHealthCheck("cache", (_, _) => Task.CompletedTask);
        using var provider = services.BuildServiceProvider();

        // then
        _Registrations(provider).Select(static x => x.Name).Should().Equal("cache");
    }

    [Fact]
    public void should_drop_a_third_party_check_that_code_turns_off()
    {
        // given
        var services = _CreateServices();
        services.ConfigureHeadlessHealthCheck("external", static options => options.Enabled = false);

        // when
        services.AddHeadlessHealthCheck(
            "external",
            static (builder, name, tags) => builder.AddCheck(name, static () => HealthCheckResult.Healthy(), tags)
        );
        using var provider = services.BuildServiceProvider();

        // then
        _Registrations(provider).Should().BeEmpty();
    }

    [Fact]
    public void should_let_code_win_over_configuration_whichever_is_registered_first()
    {
        // given - code turns the check back on before the provider registers it, configuration turns it off
        var services = _CreateServices(
            ("Headless:HealthChecks:db:Enabled", "false"),
            ("Headless:HealthChecks:db:FailureStatus", "Degraded")
        );
        services.ConfigureHeadlessHealthCheck("db", static options => options.Enabled = true);

        // when
        services.AddHeadlessHealthCheck("db", (_, _) => Task.CompletedTask);
        using var provider = services.BuildServiceProvider();

        // then - code kept the check; configuration still set what code left alone
        _Registrations(provider).Should().ContainSingle().Which.FailureStatus.Should().Be(HealthStatus.Degraded);
    }

    [Fact]
    public void should_apply_failure_status_timeout_and_tags_from_configuration()
    {
        // given
        var services = _CreateServices(
            ("Headless:HealthChecks:db:FailureStatus", "degraded"),
            ("Headless:HealthChecks:db:Timeout", "00:00:03"),
            ("Headless:HealthChecks:db:Tags:0", "critical")
        );

        // when
        services.AddHeadlessHealthCheck("db", (_, _) => Task.CompletedTask);
        using var provider = services.BuildServiceProvider();

        // then
        var registration = _Registrations(provider).Should().ContainSingle().Subject;
        registration.FailureStatus.Should().Be(HealthStatus.Degraded);
        registration.Timeout.Should().Be(TimeSpan.FromSeconds(3));
        registration.Tags.Should().Contain("critical");
    }

    [Fact]
    public void should_name_the_key_when_a_configured_value_is_invalid()
    {
        // given
        var services = _CreateServices(("Headless:HealthChecks:db:Enabled", "sometimes"));
        services.AddHeadlessHealthCheck("db", (_, _) => Task.CompletedTask);
        using var provider = services.BuildServiceProvider();

        // when
        var act = () => _Registrations(provider);

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*Headless:HealthChecks:db:Enabled*");
    }

    [Fact]
    public void should_collapse_checks_that_probe_one_resource_into_the_first_with_every_tag()
    {
        // given
        var services = _CreateServices();
        var cache = HeadlessHealthCheckResource.Shared(static _ => "redis://cache:6379");

        // when
        services.AddHeadlessHealthCheck("cache-redis", cache, (_, _) => Task.CompletedTask, "cache");
        services.AddHeadlessHealthCheck("locks-redis", cache, (_, _) => Task.CompletedTask, "locks");
        using var provider = services.BuildServiceProvider();

        // then
        var registration = _Registrations(provider).Should().ContainSingle().Subject;
        registration.Name.Should().Be("cache-redis");
        registration.Tags.Should().Contain(["cache", "locks"]);
    }

    [Fact]
    public void should_keep_the_owner_check_whichever_is_registered_first()
    {
        // given
        var services = _CreateServices();
        const string Database = "postgresql://db1:5432/orders";

        // when - the feature that only uses the database registers before the package that owns the connection
        services.AddHeadlessHealthCheck(
            "jobs-storage",
            HeadlessHealthCheckResource.Shared(static _ => Database),
            (_, _) => Task.CompletedTask,
            HeadlessHealthCheckTags.Jobs
        );
        services.AddHeadlessHealthCheck(
            "sql-postgresql",
            HeadlessHealthCheckResource.Owned(static _ => Database),
            (_, _) => Task.CompletedTask,
            HeadlessHealthCheckTags.Database
        );
        using var provider = services.BuildServiceProvider();

        // then
        var registration = _Registrations(provider).Should().ContainSingle().Subject;
        registration.Name.Should().Be("sql-postgresql");
        registration.Tags.Should().Contain([HeadlessHealthCheckTags.Jobs, HeadlessHealthCheckTags.Database]);
    }

    [Fact]
    public void should_keep_checks_on_different_resources_and_those_whose_key_fails()
    {
        // given
        var services = _CreateServices();

        // when
        services.AddHeadlessHealthCheck(
            "orders-db",
            HeadlessHealthCheckResource.Shared(static _ => "postgresql://db1:5432/orders"),
            (_, _) => Task.CompletedTask
        );
        services.AddHeadlessHealthCheck(
            "billing-db",
            HeadlessHealthCheckResource.Shared(static _ => "postgresql://db1:5432/billing"),
            (_, _) => Task.CompletedTask
        );
        services.AddHeadlessHealthCheck(
            "unknown-db",
            HeadlessHealthCheckResource.Shared(static _ => throw new InvalidOperationException("no connection string")),
            (_, _) => Task.CompletedTask
        );
        using var provider = services.BuildServiceProvider();

        // then
        _Registrations(provider)
            .Select(static x => x.Name)
            .Should()
            .BeEquivalentTo("orders-db", "billing-db", "unknown-db");
    }

    [Fact]
    public void should_keep_the_other_check_when_configuration_turns_the_owner_off()
    {
        // given
        var services = _CreateServices(("Headless:HealthChecks:sql-postgresql:Enabled", "false"));
        const string Database = "postgresql://db1:5432/orders";

        // when
        services.AddHeadlessHealthCheck(
            "sql-postgresql",
            HeadlessHealthCheckResource.Owned(static _ => Database),
            (_, _) => Task.CompletedTask
        );
        services.AddHeadlessHealthCheck(
            "jobs-storage",
            HeadlessHealthCheckResource.Shared(static _ => Database),
            (_, _) => Task.CompletedTask
        );
        using var provider = services.BuildServiceProvider();

        // then - turning one check off never leaves the database unprobed
        _Registrations(provider).Should().ContainSingle().Which.Name.Should().Be("jobs-storage");
    }

    [Theory]
    [InlineData(
        "postgresql",
        "Host=DB1;Port=5432;Database=Orders;Username=app;Password=secret",
        "postgresql://db1:5432/orders"
    )]
    [InlineData(
        "postgresql",
        "Host=db1;Port=5432;Database=orders;Username=reporting;Password=other",
        "postgresql://db1:5432/orders"
    )]
    [InlineData(
        "Npgsql.EntityFrameworkCore.PostgreSQL",
        "Host=db1;Port=5432;Database=orders",
        "postgresql://db1:5432/orders"
    )]
    [InlineData(
        "sqlserver",
        "Server=tcp:sql1,1433;Initial Catalog=Orders;User ID=app;Password=secret",
        "sqlserver://sql1:1433/orders"
    )]
    [InlineData(
        "Microsoft.EntityFrameworkCore.SqlServer",
        "Data Source=sql1,1433;Initial Catalog=orders",
        "sqlserver://sql1:1433/orders"
    )]
    public void should_key_a_database_by_provider_host_port_and_name_without_credentials(
        string provider,
        string connectionString,
        string expected
    )
    {
        HeadlessHealthCheckResource.DatabaseKey(provider, connectionString).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Database=orders")]
    public void should_give_no_database_key_without_a_host(string? connectionString)
    {
        HeadlessHealthCheckResource.DatabaseKey("postgresql", connectionString).Should().BeNull();
    }

    [Fact]
    public void should_apply_a_package_default_before_configuration_and_code_whatever_the_order()
    {
        // given - code and configuration are registered before the package's default, which comes before the check
        var services = _CreateServices(("Headless:HealthChecks:db:Timeout", "00:00:05"));
        services.ConfigureHeadlessHealthCheck("db", static options => options.Tags.Add("from-code"));
        services.ConfigureHeadlessHealthCheckDefault(
            "db",
            static options =>
            {
                options.FailureStatus = HealthStatus.Degraded;
                options.Timeout = TimeSpan.FromSeconds(1);
            }
        );

        // when
        services.AddHeadlessHealthCheck("db", (_, _) => Task.CompletedTask);
        using var provider = services.BuildServiceProvider();

        // then - the default stands where nothing overrides it, configuration overrides it, code adds on top
        var registration = _Registrations(provider).Should().ContainSingle().Subject;
        registration.FailureStatus.Should().Be(HealthStatus.Degraded);
        registration.Timeout.Should().Be(TimeSpan.FromSeconds(5));
        registration.Tags.Should().Contain("from-code");
    }

    [Fact]
    public void should_let_configuration_override_a_package_default()
    {
        // given
        var services = _CreateServices(("Headless:HealthChecks:db:FailureStatus", "Unhealthy"));

        // when
        services.AddHeadlessHealthCheck("db", (_, _) => Task.CompletedTask);
        services.ConfigureHeadlessHealthCheckDefault(
            "db",
            static options => options.FailureStatus = HealthStatus.Degraded
        );
        using var provider = services.BuildServiceProvider();

        // then
        _Registrations(provider).Should().ContainSingle().Which.FailureStatus.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public void should_not_run_the_probe_at_registration()
    {
        // given
        var services = _CreateServices();
        var calls = 0;

        // when
        services.AddHeadlessHealthCheck(
            "db",
            (_, _) =>
            {
                calls++;
                return Task.CompletedTask;
            }
        );
        using var provider = services.BuildServiceProvider();
        _ = _Registrations(provider);

        // then
        calls.Should().Be(0);
    }

    [Fact]
    public async Task should_report_healthy_when_the_probe_completes()
    {
        // given
        var services = _CreateServices();
        services.AddHeadlessHealthCheck("db", (_, _) => Task.CompletedTask);
        await using var provider = services.BuildServiceProvider();

        // when
        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync(AbortToken);

        // then
        report.Entries["db"].Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task should_report_failure_status_with_fixed_description_when_the_probe_throws()
    {
        // given
        var services = _CreateServices();
        var failure = new InvalidOperationException("secret-host:5432 refused");
        services.AddHeadlessHealthCheck("db", (_, _) => Task.FromException(failure));
        await using var provider = services.BuildServiceProvider();

        // when
        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync(AbortToken);

        // then
        var entry = report.Entries["db"];
        entry.Status.Should().Be(HealthStatus.Unhealthy);
        entry.Exception.Should().BeSameAs(failure);
        entry.Description.Should().Be("The 'db' dependency probe failed.");
    }

    [Fact]
    public async Task should_give_the_probe_the_services_of_the_check_scope()
    {
        // given
        var services = _CreateServices();
        services.AddScoped<ScopedMarker>();
        ScopedMarker? seen = null;
        services.AddHeadlessHealthCheck(
            "db",
            (sp, _) =>
            {
                seen = sp.GetRequiredService<ScopedMarker>();
                return Task.CompletedTask;
            }
        );
        await using var provider = services.BuildServiceProvider(validateScopes: true);

        // when
        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync(AbortToken);

        // then
        report.Entries["db"].Status.Should().Be(HealthStatus.Healthy);
        seen.Should().NotBeNull();
    }

    [Fact]
    public void should_remove_matching_checks_even_when_they_are_added_after_the_removal()
    {
        // given
        var services = _CreateServices();
        services.RemoveHealthChecks(registration => registration.Tags.Contains(HeadlessHealthCheckTags.Headless));

        // when
        services.AddHeadlessHealthCheck("db", (_, _) => Task.CompletedTask);
        services.AddHealthChecks().AddCheck("self", () => HealthCheckResult.Healthy());
        using var provider = services.BuildServiceProvider();

        // then
        _Registrations(provider).Should().ContainSingle().Which.Name.Should().Be("self");
    }

    [Fact]
    public async Task should_apply_the_configured_failure_status_timeout_and_tags()
    {
        // given - configured before the contribution, which must not matter
        var services = _CreateServices();
        services.ConfigureHeadlessHealthCheck(
            "db",
            options =>
            {
                options.FailureStatus = HealthStatus.Degraded;
                options.Timeout = TimeSpan.FromSeconds(3);
                options.Tags.Add("critical");
            }
        );
        services.AddHeadlessHealthCheck("db", (_, _) => throw new InvalidOperationException("down"));
        await using var provider = services.BuildServiceProvider();

        // when
        var registration = _Registrations(provider).Should().ContainSingle().Subject;
        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync(AbortToken);

        // then
        registration.FailureStatus.Should().Be(HealthStatus.Degraded);
        registration.Timeout.Should().Be(TimeSpan.FromSeconds(3));
        registration.Tags.Should().Contain(["critical", HeadlessHealthCheckTags.Ready]);
        report.Entries["db"].Status.Should().Be(HealthStatus.Degraded);
    }

    [Fact]
    public async Task should_report_the_failure_status_with_fixed_text_when_the_probe_times_out()
    {
        // given
        var services = _CreateServices();
        services.ConfigureHeadlessHealthCheck("slow", options => options.Timeout = TimeSpan.FromMilliseconds(50));
        services.AddHeadlessHealthCheck(
            "slow",
            static (_, cancellationToken) => Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
        );
        await using var provider = services.BuildServiceProvider();

        // when
        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync(AbortToken);

        // then
        var entry = report.Entries["slow"];
        entry.Status.Should().Be(HealthStatus.Unhealthy);
        entry.Description.Should().Be("A timeout occurred while running check.");
    }

    [Fact]
    public void should_reject_healthy_as_a_failure_status()
    {
        // given
        var services = _CreateServices();
        services.ConfigureHeadlessHealthCheck("db", options => options.FailureStatus = HealthStatus.Healthy);
        using var provider = services.BuildServiceProvider();

        // when
        var act = () => provider.GetRequiredService<IOptionsMonitor<HeadlessHealthCheckOptions>>().Get("db");

        // then
        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public async Task should_report_a_third_party_check_failure_with_fixed_text_and_the_configured_status()
    {
        // given - the wrapped check returns the driver message, and its dependencies can throw while resolving
        var services = _CreateServices();
        services.ConfigureHeadlessHealthCheck("leaky", options => options.FailureStatus = HealthStatus.Degraded);
        services.ConfigureHeadlessHealthCheck("broken", options => options.FailureStatus = HealthStatus.Degraded);
        services.AddHeadlessHealthCheck(
            "leaky",
            static (builder, name, tags) =>
                builder.AddCheck(
                    name,
                    () => HealthCheckResult.Unhealthy("host=secret-db", new InvalidOperationException()),
                    tags
                )
        );
        services.AddHeadlessHealthCheck(
            "broken",
            static (builder, name, tags) =>
                builder.Add(
                    new HealthCheckRegistration(
                        name,
                        _ => throw new InvalidOperationException("host=secret-db"),
                        failureStatus: null,
                        tags
                    )
                )
        );
        await using var provider = services.BuildServiceProvider();

        // when
        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync(AbortToken);

        // then
        foreach (var name in new[] { "leaky", "broken" })
        {
            var entry = report.Entries[name];
            entry.Status.Should().Be(HealthStatus.Degraded);
            entry.Description.Should().Be($"The '{name}' dependency probe failed.");
            entry.Exception.Should().BeOfType<InvalidOperationException>();
        }
    }

    [Fact]
    public void should_register_a_third_party_check_once_per_name()
    {
        // given
        var services = _CreateServices();
        var calls = 0;

        // when
        for (var i = 0; i < 2; i++)
        {
            services.AddHeadlessHealthCheck(
                "db",
                (builder, name, tags) =>
                {
                    calls++;
                    builder.AddCheck(name, () => HealthCheckResult.Healthy(), tags);
                }
            );
        }

        using var provider = services.BuildServiceProvider();

        // then
        calls.Should().Be(1);
        _Registrations(provider).Should().ContainSingle();
    }

    private static ServiceCollection _CreateServices(params (string Key, string Value)[] settings)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        if (settings.Length > 0)
        {
            services.AddSingleton<IConfiguration>(
                new ConfigurationBuilder()
                    .AddInMemoryCollection(settings.Select(x => new KeyValuePair<string, string?>(x.Key, x.Value)))
                    .Build()
            );
        }

        return services;
    }

    private static ICollection<HealthCheckRegistration> _Registrations(IServiceProvider provider)
    {
        return provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;
    }

    private sealed class ScopedMarker;
}
