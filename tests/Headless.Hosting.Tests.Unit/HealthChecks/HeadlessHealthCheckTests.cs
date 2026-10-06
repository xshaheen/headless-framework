// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Testing.Tests;
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

    private static ServiceCollection _CreateServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        return services;
    }

    private static ICollection<HealthCheckRegistration> _Registrations(IServiceProvider provider)
    {
        return provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;
    }

    private sealed class ScopedMarker;
}
