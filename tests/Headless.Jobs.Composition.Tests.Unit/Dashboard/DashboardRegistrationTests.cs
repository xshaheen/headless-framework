// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard.Authentication;
using Headless.Jobs;
using Headless.Jobs.Infrastructure;
using Headless.Jobs.Infrastructure.Dashboard;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests.Dashboard;

public sealed class DashboardRegistrationTests : TestBase
{
    [Fact]
    public void should_fail_closed_when_authentication_was_not_explicitly_configured()
    {
        var services = new ServiceCollection();

        var act = () => services.AddHeadlessJobs(options => options.DisableBackgroundServices().AddDashboard());

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*authentication was not configured*")
            .WithMessage("*WithNoAuth*");
    }

    [Fact]
    public void should_wire_the_repository_pipeline_and_no_cors_policy_when_no_auth_is_selected()
    {
        var services = new ServiceCollection();

        services.AddHeadlessJobs(options =>
            options
                .DisableBackgroundServices()
                .AddDashboard(dashboard =>
                    dashboard
                        .WithNoAuth()
                        .ConfigureDashboardJsonOptions(json =>
                            json.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
                        )
                )
        );

        services
            .Should()
            .ContainSingle(descriptor =>
                descriptor.ServiceType == typeof(IJobsDashboardRepository<TimeJobEntity, CronJobEntity>)
                && descriptor.ImplementationType == typeof(JobsDashboardRepository<TimeJobEntity, CronJobEntity>)
                && descriptor.Lifetime == ServiceLifetime.Scoped
            );
        services
            .Should()
            .ContainSingle(descriptor =>
                descriptor.ServiceType == typeof(IJobsNotificationHubSender)
                && descriptor.ImplementationType == typeof(JobsNotificationHubSender)
                && descriptor.Lifetime == ServiceLifetime.Singleton
            );
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IStartupFilter));

        using var provider = services.BuildServiceProvider();
        var dashboard = provider.GetRequiredService<DashboardOptionsBuilder>();
        dashboard.Auth.Mode.Should().Be(AuthMode.None);
        dashboard.DashboardJsonOptions.Should().NotBeNull();
        dashboard.DashboardJsonOptions!.PropertyNamingPolicy.Should().Be(JsonNamingPolicy.SnakeCaseLower);
        dashboard
            .DashboardJsonOptions.Converters.Should()
            .ContainSingle(converter => converter is StringToByteArrayConverter);

        // Same-origin by default: no policy, so the host needs no UseCors for the dashboard's endpoints.
        provider
            .GetRequiredService<IOptions<CorsOptions>>()
            .Value.GetPolicy("HeadlessJobsDashboardCORS")
            .Should()
            .BeNull();
    }

    [Fact]
    public void should_add_auth_services_and_the_session_scheme_when_host_auth_is_selected()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddHeadlessJobs(options =>
            options
                .DisableBackgroundServices()
                .AddDashboard(dashboard => dashboard.WithHostAuthentication(host => host.ViewPolicy = "Operators"))
        );

        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IAuthenticationService));
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IAuthenticationSchemeProvider));

        using var provider = services.BuildServiceProvider();
        var dashboard = provider.GetRequiredService<DashboardOptionsBuilder>();
        dashboard.Auth.Mode.Should().Be(AuthMode.Host);
        provider
            .GetRequiredService<IOptionsMonitor<AuthConfig>>()
            .Get(DashboardOptionsBuilder.AuthenticationName)
            .Host.ViewPolicy.Should()
            .Be("Operators");
        provider
            .GetRequiredService<IOptions<AuthenticationOptions>>()
            .Value.SchemeMap.Should()
            .ContainKey(DashboardOptionsBuilder.AuthenticationName + ".Session");
        provider
            .GetRequiredService<IOptions<CorsOptions>>()
            .Value.GetPolicy("HeadlessJobsDashboardCORS")
            .Should()
            .BeNull();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void should_fail_registration_when_host_auth_is_combined_with_a_cross_origin_setting(bool cors)
    {
        var services = new ServiceCollection();

        var act = () =>
            services.AddHeadlessJobs(options =>
                options
                    .DisableBackgroundServices()
                    .AddDashboard(dashboard =>
                    {
                        dashboard.WithHostAuthentication();
                        if (cors)
                        {
                            dashboard.SetCorsOrigins("https://ops.example.com");
                        }
                        else
                        {
                            dashboard.SetBackendDomain("https://api.example.com");
                        }
                    })
            );

        act.Should().Throw<InvalidOperationException>().WithMessage("*host's own origin*");
    }

    [Fact]
    public void should_add_the_configured_cors_policy_under_basic_auth()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddHeadlessJobs(options =>
            options
                .DisableBackgroundServices()
                .AddDashboard(dashboard =>
                    dashboard.WithBasicAuth("ops", "secret").SetCorsOrigins("https://ops.example.com")
                )
        );

        using var provider = services.BuildServiceProvider();
        provider
            .GetRequiredService<IOptions<CorsOptions>>()
            .Value.GetPolicy("HeadlessJobsDashboardCORS")!
            .Origins.Should()
            .Equal("https://ops.example.com");
    }
}
