// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard.Authentication;
using Headless.Jobs;
using Headless.Jobs.Entities;
using Headless.Jobs.Hubs;
using Headless.Jobs.Infrastructure;
using Headless.Jobs.Infrastructure.Dashboard;
using Headless.Jobs.Interfaces;
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
    public void should_wire_the_repository_pipeline_and_same_origin_cors_when_no_auth_is_selected()
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

        var cors = provider.GetRequiredService<IOptions<CorsOptions>>().Value;
        var policy = cors.GetPolicy("HeadlessJobsDashboardCORS");
        policy.Should().NotBeNull();
        policy!.Origins.Should().BeEmpty("the dashboard defaults to same-origin access");
    }

    [Fact]
    public void should_add_auth_services_and_configured_cors_when_host_auth_is_selected()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddHeadlessJobs(options =>
            options
                .DisableBackgroundServices()
                .AddDashboard(dashboard =>
                    dashboard.WithHostAuthentication("Operators").SetCorsOrigins("https://ops.example.com")
                )
        );

        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IAuthenticationService));
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IAuthenticationSchemeProvider));

        using var provider = services.BuildServiceProvider();
        var dashboard = provider.GetRequiredService<DashboardOptionsBuilder>();
        dashboard.Auth.Mode.Should().Be(AuthMode.Host);
        dashboard.Auth.HostAuthorizationPolicy.Should().Be("Operators");
        provider.GetRequiredService<IAuthService>().Should().NotBeNull();
        provider
            .GetRequiredService<IOptions<CorsOptions>>()
            .Value.GetPolicy("HeadlessJobsDashboardCORS")!
            .Origins.Should()
            .Equal("https://ops.example.com");
    }
}
