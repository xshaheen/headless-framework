// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api;
using Headless.Api.Cors;
using Headless.Constants;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Options;

namespace Tests.Cors;

public sealed class SetupCorsTests : TestBase
{
    [Fact]
    public async Task should_build_the_restricted_policy_from_options()
    {
        var policy = await _GetPolicyAsync(
            HeadlessCorsConstants.RestrictedCors,
            services =>
                services.AddHeadlessCors(options =>
                {
                    options.AllowedOrigins = ["https://app.example.com"];
                    options.AllowedOriginTemplates = ["https://*.tenants.example.com"];
                    options.AllowCredentials = true;
                    options.AllowedHeaders = ["Content-Type"];
                    options.AllowedMethods = ["GET"];
                    options.ExposedHeaders = ["ETag"];
                    options.MaxAge = TimeSpan.FromMinutes(10);
                })
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
            services => services.AddHeadlessCors(options => options.AllowedOrigins = ["https://app.example.com"])
        );

        policy.SupportsCredentials.Should().BeFalse();
        policy.AllowAnyHeader.Should().BeTrue();
        policy.AllowAnyMethod.Should().BeTrue();
        policy.PreflightMaxAge.Should().BeNull();
    }

    [Fact]
    public async Task should_register_the_any_origin_policy_on_its_own()
    {
        var policy = await _GetPolicyAsync(
            HeadlessCorsConstants.AllowAnyCors,
            services =>
                services.AddHeadlessAllowAnyCors(options =>
                {
                    options.ExposedHeaders = ["ETag", "Link"];
                    options.MaxAge = TimeSpan.FromMinutes(10);
                })
        );

        policy.AllowAnyOrigin.Should().BeTrue();
        policy.SupportsCredentials.Should().BeFalse();
        policy.AllowAnyHeader.Should().BeTrue();
        policy.AllowAnyMethod.Should().BeTrue();
        policy.ExposedHeaders.Should().Equal("ETag", "Link");
        policy.PreflightMaxAge.Should().Be(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public async Task should_build_several_named_policies_independently()
    {
        await using var provider = new ServiceCollection()
            .AddHeadlessCors(
                "admin",
                options =>
                {
                    options.AllowedOrigins = ["https://admin.example.com"];
                    options.AllowCredentials = true;
                }
            )
            .AddHeadlessCors(
                "public",
                options =>
                {
                    options.AllowAnyOrigin = true;
                    options.AllowedMethods = ["GET"];
                }
            )
            .BuildServiceProvider();

        var cors = provider.GetRequiredService<IOptions<CorsOptions>>().Value;
        var admin = cors.GetPolicy("admin");
        var open = cors.GetPolicy("public");

        admin.Should().NotBeNull();
        admin!.SupportsCredentials.Should().BeTrue();
        admin.IsOriginAllowed("https://admin.example.com").Should().BeTrue();
        admin.IsOriginAllowed("https://app.example.com").Should().BeFalse();

        open.Should().NotBeNull();
        open!.AllowAnyOrigin.Should().BeTrue();
        open.SupportsCredentials.Should().BeFalse();
        open.Methods.Should().Equal("GET");

        cors.GetPolicy(HeadlessCorsConstants.RestrictedCors).Should().BeNull();
    }

    [Fact]
    public async Task should_merge_repeated_registrations_of_one_policy_and_report_each_failure_once()
    {
        await using var provider = new ServiceCollection()
            .AddHeadlessCors(options => options.AllowCredentials = true)
            .AddHeadlessCors(options => options.MaxAge = TimeSpan.FromMinutes(1))
            .BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptions<CorsOptions>>().Value;

        var failures = act.Should().Throw<OptionsValidationException>().Which.Failures;
        failures.Should().ContainSingle().Which.Should().Contain("at least one");
    }

    [Fact]
    public async Task should_match_a_hybrid_mobile_webview_origin()
    {
        var policy = await _GetPolicyAsync(
            HeadlessCorsConstants.RestrictedCors,
            services => services.AddHeadlessCors(options => options.AllowedOrigins = ["capacitor://localhost"])
        );

        policy.IsOriginAllowed("capacitor://localhost").Should().BeTrue();
        policy.IsOriginAllowed("ionic://localhost").Should().BeFalse();
    }

    [Fact]
    public async Task should_bind_a_named_policy_from_configuration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["AllowedOrigins:0"] = "https://app.example.com",
                    ["AllowedOriginTemplates:0"] = "https://*.example.com",
                    ["AllowCredentials"] = "true",
                    ["MaxAge"] = "00:05:00",
                    ["HasOriginSource"] = "true",
                }
            )
            .Build();
        await using var provider = new ServiceCollection().AddHeadlessCors(configuration).BuildServiceProvider();

        var options = provider
            .GetRequiredService<IOptionsMonitor<HeadlessCorsOptions>>()
            .Get(HeadlessCorsConstants.RestrictedCors);

        options.AllowedOrigins.Should().Equal("https://app.example.com");
        options.AllowedOriginTemplates.Should().Equal("https://*.example.com");
        options.AllowCredentials.Should().BeTrue();
        options.MaxAge.Should().Be(TimeSpan.FromMinutes(5));
        options
            .HasOriginSource.Should()
            .BeFalse("configuration must not claim an origin source that is not registered");
    }

    [Fact]
    public async Task should_accept_a_policy_whose_origins_all_come_from_a_source()
    {
        var policy = await _GetPolicyAsync(
            HeadlessCorsConstants.RestrictedCors,
            services => services.AddHeadlessCorsOriginSource<ApproveNothingSource>()
        );

        policy.Origins.Should().BeEmpty();
    }

    [Fact]
    public async Task should_fail_an_any_origin_policy_in_production_without_confirmation()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new HostingEnvironment { EnvironmentName = Environments.Production });
        services.AddHeadlessAllowAnyCors();
        await using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptions<CorsOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*AllowAnyOriginInProduction*");
    }

    [Fact]
    public async Task should_allow_a_confirmed_any_origin_policy_in_production()
    {
        var policy = await _GetPolicyAsync(
            HeadlessCorsConstants.AllowAnyCors,
            services =>
            {
                services.AddSingleton<IHostEnvironment>(
                    new HostingEnvironment { EnvironmentName = Environments.Production }
                );
                services.AddHeadlessAllowAnyCors(options => options.AllowAnyOriginInProduction = true);
            }
        );

        policy.AllowAnyOrigin.Should().BeTrue();
    }

    [Fact]
    public void should_reject_a_blank_policy_name()
    {
        var act = () => new ServiceCollection().AddHeadlessCors(" ", _ => { });

        act.Should().Throw<ArgumentException>();
    }

    private static async Task<CorsPolicy> _GetPolicyAsync(string name, Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        register(services);
        await using var provider = services.BuildServiceProvider();

        var policy = provider.GetRequiredService<IOptions<CorsOptions>>().Value.GetPolicy(name);

        policy.Should().NotBeNull();

        return policy!;
    }

    private sealed class ApproveNothingSource : ICorsOriginSource
    {
        public ValueTask<bool> IsOriginAllowedAsync(
            string origin,
            HttpContext context,
            CancellationToken cancellationToken
        )
        {
            return ValueTask.FromResult(false);
        }
    }
}
