// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard;
using Headless.Jobs;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Cors.Infrastructure;

namespace Tests.Dashboard;

public sealed class DashboardOptionsTests : TestBase
{
    [Fact]
    public void should_default_to_same_origin_and_require_an_explicit_auth_choice()
    {
        var builder = new DashboardOptionsBuilder();

        builder.BasePath.Should().Be("/jobs/dashboard");
        builder.BackendDomain.Should().BeNull();
        builder.CorsPolicyBuilder.Should().BeNull();
        builder.Auth.Mode.Should().Be(AuthMode.None);
        builder.AuthConfigured.Should().BeFalse();
        ((Action)builder.Validate).Should().Throw<InvalidOperationException>().WithMessage("*authentication*");
    }

    [Fact]
    public void should_build_a_credentialed_cors_policy_for_explicit_origins()
    {
        var builder = new DashboardOptionsBuilder().SetCorsOrigins(
            "https://admin.example.com",
            "https://ops.example.com"
        );
        var policyBuilder = new CorsPolicyBuilder();

        builder.CorsPolicyBuilder!(policyBuilder);
        var policy = policyBuilder.Build();

        policy.Origins.Should().Equal("https://admin.example.com", "https://ops.example.com");
        policy.AllowAnyHeader.Should().BeTrue();
        policy.AllowAnyMethod.Should().BeTrue();
        policy.SupportsCredentials.Should().BeTrue();
    }

    [Fact]
    public void should_reject_an_empty_cors_origin_list()
    {
        var act = () => new DashboardOptionsBuilder().SetCorsOrigins();

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_configure_paths_and_custom_cors_policy_fluently()
    {
        Action<CorsPolicyBuilder> cors = static policy => policy.WithMethods("GET");

        var builder = new DashboardOptionsBuilder()
            .SetBasePath("/admin/jobs")
            .SetBackendDomain("https://api.example.com")
            .SetCorsPolicy(cors);

        builder.BasePath.Should().Be("/admin/jobs");
        builder.BackendDomain.Should().Be("https://api.example.com");
        builder.CorsPolicyBuilder.Should().BeSameAs(cors);
    }

    [Fact]
    public void should_explicitly_allow_an_unauthenticated_dashboard()
    {
        var builder = new DashboardOptionsBuilder().WithNoAuth();

        builder.AuthConfigured.Should().BeTrue();
        builder.Auth.Mode.Should().Be(AuthMode.None);
        builder.Auth.IsEnabled.Should().BeFalse();
        ((Action)builder.Validate).Should().NotThrow();
    }

    [Fact]
    public void should_configure_basic_auth_credentials()
    {
        var builder = new DashboardOptionsBuilder().WithBasicAuth("admin", "password");

        builder.AuthConfigured.Should().BeTrue();
        builder.Auth.Mode.Should().Be(AuthMode.Basic);
        builder.Auth.BasicCredentials.Should().Be("admin:password".ToBase64());
        ((Action)builder.Validate).Should().NotThrow();
    }

    [Fact]
    public void should_configure_api_key_authentication()
    {
        var builder = new DashboardOptionsBuilder().WithApiKey("secret-key");

        builder.AuthConfigured.Should().BeTrue();
        builder.Auth.Mode.Should().Be(AuthMode.ApiKey);
        builder.Auth.ApiKey.Should().Be("secret-key");
    }

    [Fact]
    public void should_configure_host_authentication_with_the_host_defaults()
    {
        var builder = new DashboardOptionsBuilder().WithHostAuthentication();

        builder.AuthConfigured.Should().BeTrue();
        builder.Auth.Mode.Should().Be(AuthMode.Host);
        builder.Auth.Host.ViewPolicy.Should().BeNull();
        builder.Auth.Host.OperatePolicy.Should().BeNull();
        builder.Auth.Host.SessionLifetime.Should().Be(DashboardHostAuthenticationOptions.DefaultSessionLifetime);
        ((Action)builder.Validate).Should().NotThrow();
    }

    [Fact]
    public void should_configure_the_host_policies_and_session()
    {
        var builder = new DashboardOptionsBuilder().WithHostAuthentication(host =>
        {
            host.ViewPolicy = "Viewers";
            host.OperatePolicy = "Operators";
            host.ChallengeScheme = "oidc";
            host.SessionIdleTimeout = TimeSpan.FromMinutes(5);
        });

        builder.Auth.Mode.Should().Be(AuthMode.Host);
        builder.Auth.Host.ViewPolicy.Should().Be("Viewers");
        builder.Auth.Host.OperatePolicy.Should().Be("Operators");
        builder.Auth.Host.ChallengeScheme.Should().Be("oidc");
        builder.Auth.Host.SessionIdleTimeout.Should().Be(TimeSpan.FromMinutes(5));
    }

    public static TheoryData<string> CrossOriginSettings => ["origins", "policy", "backend"];

    [Theory]
    [MemberData(nameof(CrossOriginSettings))]
    public void should_keep_cross_origin_settings_under_a_dashboard_credential(string setting)
    {
        var basic = _WithCrossOrigin(new DashboardOptionsBuilder().WithBasicAuth("ops", "secret"), setting);
        var apiKey = _WithCrossOrigin(new DashboardOptionsBuilder().WithApiKey("secret"), setting);

        ((Action)basic.Validate).Should().NotThrow();
        ((Action)apiKey.Validate).Should().NotThrow();
    }

    private static DashboardOptionsBuilder _WithCrossOrigin(DashboardOptionsBuilder builder, string setting)
    {
        return setting switch
        {
            "origins" => builder.SetCorsOrigins("https://ops.example.com"),
            "policy" => builder.SetCorsPolicy(static policy => policy.AllowAnyOrigin()),
            _ => builder.SetBackendDomain("https://api.example.com"),
        };
    }

    [Fact]
    public void should_preserve_dashboard_json_customizations_across_multiple_callbacks()
    {
        var builder = new DashboardOptionsBuilder()
            .ConfigureDashboardJsonOptions(options => options.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower)
            .ConfigureDashboardJsonOptions(options => options.WriteIndented = true)
            .ConfigureDashboardJsonOptions(null);

        builder.DashboardJsonOptions.Should().NotBeNull();
        builder.DashboardJsonOptions!.PropertyNamingPolicy.Should().Be(JsonNamingPolicy.SnakeCaseLower);
        builder.DashboardJsonOptions.WriteIndented.Should().BeTrue();
    }
}
