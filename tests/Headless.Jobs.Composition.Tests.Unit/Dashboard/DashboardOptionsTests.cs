// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard.Authentication;
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

    [Theory]
    [InlineData(null)]
    [InlineData("DashboardAdmin")]
    public void should_configure_host_authentication(string? policy)
    {
        var builder = new DashboardOptionsBuilder().WithHostAuthentication(policy);

        builder.AuthConfigured.Should().BeTrue();
        builder.Auth.Mode.Should().Be(AuthMode.Host);
        builder.Auth.HostAuthorizationPolicy.Should().Be(policy);
    }

    [Fact]
    public void should_configure_custom_authentication_and_session_timeout()
    {
        static bool validator(string token, IServiceProvider _) => token == "valid";

        var builder = new DashboardOptionsBuilder().WithCustomAuth(validator).WithSessionTimeout(30);

        builder.AuthConfigured.Should().BeTrue();
        builder.Auth.Mode.Should().Be(AuthMode.Custom);
        builder.Auth.CustomValidator.Should().BeSameAs((Func<string, IServiceProvider, bool>)validator);
        builder.Auth.SessionTimeoutMinutes.Should().Be(30);
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
