// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api;
using Headless.Api.Cors;
using Headless.Constants;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests.Cors;

public sealed class SetupCorsTests : TestBase
{
    [Fact]
    public async Task should_build_the_restricted_policy_from_options()
    {
        var policy = await _GetPolicyAsync(
            HeadlessCorsConstants.RestrictedCors,
            options =>
            {
                options.AllowedOrigins = ["https://app.example.com"];
                options.AllowedOriginTemplates = ["https://*.tenants.example.com"];
                options.AllowCredentials = true;
                options.AllowedHeaders = ["Content-Type"];
                options.AllowedMethods = ["GET"];
                options.ExposedHeaders = ["ETag"];
                options.MaxAge = TimeSpan.FromMinutes(10);
            }
        );

        policy.SupportsCredentials.Should().BeTrue();
        policy.AllowAnyOrigin.Should().BeFalse();
        policy.Headers.Should().Equal("Content-Type");
        policy.Methods.Should().Equal("GET");
        policy.ExposedHeaders.Should().Equal("ETag");
        policy.PreflightMaxAge.Should().Be(TimeSpan.FromMinutes(10));

        policy.IsOriginAllowed("https://app.example.com").Should().BeTrue();
        policy.IsOriginAllowed("https://acme.tenants.example.com").Should().BeTrue();
        policy.IsOriginAllowed("https://a.b.tenants.example.com").Should().BeTrue();
        policy.IsOriginAllowed("https://tenants.example.com").Should().BeFalse();
        policy.IsOriginAllowed("http://acme.tenants.example.com").Should().BeFalse();
        policy.IsOriginAllowed("https://acme.tenants.example.com.evil.com").Should().BeFalse();
        policy.IsOriginAllowed("https://evil.com").Should().BeFalse();
    }

    [Fact]
    public async Task should_allow_any_header_and_method_and_no_credentials_by_default()
    {
        var policy = await _GetPolicyAsync(
            HeadlessCorsConstants.RestrictedCors,
            options => options.AllowedOrigins = ["https://app.example.com"]
        );

        policy.SupportsCredentials.Should().BeFalse();
        policy.AllowAnyHeader.Should().BeTrue();
        policy.AllowAnyMethod.Should().BeTrue();
        policy.PreflightMaxAge.Should().BeNull();
    }

    [Fact]
    public async Task should_register_an_any_origin_policy_without_credentials_sharing_exposed_headers()
    {
        var policy = await _GetPolicyAsync(
            HeadlessCorsConstants.AllowAnyCors,
            options =>
            {
                options.AllowedOrigins = ["https://app.example.com"];
                options.AllowCredentials = true;
                options.AllowedMethods = ["GET"];
                options.ExposedHeaders = ["ETag", "Link"];
                options.MaxAge = TimeSpan.FromMinutes(10);
            }
        );

        policy.AllowAnyOrigin.Should().BeTrue();
        policy.SupportsCredentials.Should().BeFalse();
        policy.AllowAnyMethod.Should().BeTrue();
        policy.ExposedHeaders.Should().Equal("ETag", "Link");
        policy.PreflightMaxAge.Should().Be(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public async Task should_match_a_hybrid_mobile_webview_origin()
    {
        var policy = await _GetPolicyAsync(
            HeadlessCorsConstants.RestrictedCors,
            options => options.AllowedOrigins = ["capacitor://localhost"]
        );

        policy.IsOriginAllowed("capacitor://localhost").Should().BeTrue();
        policy.IsOriginAllowed("ionic://localhost").Should().BeFalse();
    }

    [Fact]
    public async Task should_bind_options_from_configuration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["AllowedOrigins:0"] = "https://app.example.com",
                    ["AllowedOriginTemplates:0"] = "https://*.example.com",
                    ["AllowCredentials"] = "true",
                    ["MaxAge"] = "00:05:00",
                }
            )
            .Build();
        await using var provider = new ServiceCollection().AddHeadlessCors(configuration).BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<HeadlessCorsOptions>>().Value;

        options.AllowedOrigins.Should().Equal("https://app.example.com");
        options.AllowedOriginTemplates.Should().Equal("https://*.example.com");
        options.AllowCredentials.Should().BeTrue();
        options.MaxAge.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task should_fail_when_resolving_invalid_options()
    {
        await using var provider = new ServiceCollection()
            .AddHeadlessCors(options => options.AllowCredentials = true)
            .BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptions<CorsOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*at least one*");
    }

    private static async Task<CorsPolicy> _GetPolicyAsync(string name, Action<HeadlessCorsOptions> configure)
    {
        await using var provider = new ServiceCollection().AddHeadlessCors(configure).BuildServiceProvider();

        var policy = provider.GetRequiredService<IOptions<CorsOptions>>().Value.GetPolicy(name);

        policy.Should().NotBeNull();

        return policy!;
    }
}
