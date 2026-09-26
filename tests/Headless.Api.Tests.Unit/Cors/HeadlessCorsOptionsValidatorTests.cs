// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation.TestHelper;
using Headless.Api.Cors;
using Headless.Testing.Tests;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace Tests.Cors;

public sealed class HeadlessCorsOptionsValidatorTests : TestBase
{
    private readonly HeadlessCorsOptionsValidator _sut = new();

    [Fact]
    public void should_accept_a_production_shape_with_credentials()
    {
        var options = new HeadlessCorsOptions
        {
            AllowedOrigins = ["https://app.example.com", "http://localhost:5173", "https://admin.example.com:8443"],
            AllowedOriginTemplates = ["https://*.tenants.example.com"],
            AllowCredentials = true,
            AllowedHeaders = ["Content-Type", "Authorization"],
            AllowedMethods = ["GET", "POST"],
            ExposedHeaders = ["ETag"],
            MaxAge = TimeSpan.FromMinutes(10),
        };

        var result = _sut.TestValidate(options);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void should_reject_options_without_any_origin_whether_or_not_credentials_are_allowed(bool credentials)
    {
        var options = new HeadlessCorsOptions { AllowCredentials = credentials };

        var result = _sut.TestValidate(options);

        var failure = result.Errors.Should().ContainSingle().Subject;
        failure.ErrorMessage.Should().Contain("at least one").And.Contain("AllowAnyCors");
    }

    [Theory]
    [InlineData("*", "wildcard")]
    [InlineData("https://*.example.com", "wildcard")]
    [InlineData("https://app.example.com/", "trailing slash")]
    [InlineData("https://app.example.com/api", "path")]
    [InlineData("https://user:pass@app.example.com", "user info")]
    [InlineData("https://app.example.com?x=1", "query")]
    [InlineData("https://app.example.com#top", "fragment")]
    [InlineData("file://localhost", "absolute origin")]
    [InlineData("mailto:admin@example.com", "absolute origin")]
    [InlineData("app.example.com", "absolute origin")]
    [InlineData("null", "absolute origin")]
    [InlineData(" https://app.example.com", "whitespace")]
    [InlineData("", "empty")]
    public void should_reject_an_origin_that_is_not_a_bare_serialized_origin(string origin, string reason)
    {
        var options = new HeadlessCorsOptions { AllowedOrigins = [origin] };

        var result = _sut.TestValidate(options);

        var failure = result.Errors.Should().ContainSingle().Subject;
        failure.ErrorMessage.Should().Contain(reason);
    }

    [Theory]
    [InlineData("https://*", "'*.'")]
    [InlineData("https://*.com", "two labels")]
    [InlineData("https://*.example.com.", "two labels")]
    [InlineData("https://*.", "absolute origin")]
    [InlineData("https://example.com", "'*.'")]
    [InlineData("https://app.*.example.com", "'*.'")]
    [InlineData("*.example.com", "'*.'")]
    [InlineData("https://*.*.example.com", "one leading wildcard")]
    [InlineData("https://*.example.com/", "trailing slash")]
    [InlineData("https://*.user@example.com", "user info")]
    public void should_reject_a_template_without_a_safe_literal_suffix(string template, string reason)
    {
        var options = new HeadlessCorsOptions { AllowedOriginTemplates = [template] };

        var result = _sut.TestValidate(options);

        var failure = result.Errors.Should().ContainSingle().Subject;
        failure.ErrorMessage.Should().Contain(template).And.Contain(reason);
    }

    [Theory]
    [InlineData("capacitor://localhost")]
    [InlineData("ionic://localhost")]
    [InlineData("http://127.0.0.1:8080")]
    public void should_accept_a_non_http_or_loopback_origin(string origin)
    {
        var options = new HeadlessCorsOptions { AllowedOrigins = [origin] };

        var result = _sut.TestValidate(options);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void should_accept_a_template_alone()
    {
        var options = new HeadlessCorsOptions { AllowedOriginTemplates = ["https://*.example.com:8443"] };

        var result = _sut.TestValidate(options);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void should_reject_blank_header_and_method_entries()
    {
        var options = new HeadlessCorsOptions
        {
            AllowedOrigins = ["https://app.example.com"],
            AllowedHeaders = [" "],
            AllowedMethods = [""],
            ExposedHeaders = [""],
        };

        var result = _sut.TestValidate(options);

        result.Errors.Should().HaveCount(3);
    }

    [Fact]
    public void should_reject_a_non_positive_max_age()
    {
        var options = new HeadlessCorsOptions { AllowedOrigins = ["https://app.example.com"], MaxAge = TimeSpan.Zero };

        var result = _sut.TestValidate(options);

        result.ShouldHaveValidationErrorFor(x => x.MaxAge);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Staging")]
    public void should_accept_an_any_origin_policy_outside_production(string environment)
    {
        var sut = new HeadlessCorsOptionsValidator(new HostingEnvironment { EnvironmentName = environment });

        var result = sut.TestValidate(new HeadlessCorsOptions { AllowAnyOrigin = true });

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void should_reject_an_unconfirmed_any_origin_policy_in_production()
    {
        var sut = new HeadlessCorsOptionsValidator(
            new HostingEnvironment { EnvironmentName = Environments.Production }
        );

        var result = sut.TestValidate(new HeadlessCorsOptions { AllowAnyOrigin = true });

        result.ShouldHaveValidationErrorFor(x => x.AllowAnyOriginInProduction);
    }

    [Fact]
    public void should_accept_a_confirmed_any_origin_policy_in_production()
    {
        var sut = new HeadlessCorsOptionsValidator(
            new HostingEnvironment { EnvironmentName = Environments.Production }
        );

        var result = sut.TestValidate(
            new HeadlessCorsOptions { AllowAnyOrigin = true, AllowAnyOriginInProduction = true }
        );

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void should_reject_credentials_on_an_any_origin_policy()
    {
        var result = _sut.TestValidate(new HeadlessCorsOptions { AllowAnyOrigin = true, AllowCredentials = true });

        result.ShouldHaveValidationErrorFor(x => x.AllowCredentials);
    }

    [Fact]
    public void should_reject_listed_origins_on_an_any_origin_policy()
    {
        var result = _sut.TestValidate(
            new HeadlessCorsOptions { AllowAnyOrigin = true, AllowedOrigins = ["https://app.example.com"] }
        );

        var failure = result.Errors.Should().ContainSingle().Subject;
        failure.ErrorMessage.Should().Contain("already admits every origin");
    }

    [Fact]
    public void should_accept_a_policy_backed_only_by_an_origin_source()
    {
        var result = _sut.TestValidate(new HeadlessCorsOptions { HasOriginSource = true });

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("http://localhost:8081")]
    [InlineData("http://localhost:19006")]
    public void should_accept_expo_web_dev_server_origins(string origin)
    {
        var result = _sut.TestValidate(new HeadlessCorsOptions { AllowedOrigins = [origin] });

        result.ShouldNotHaveAnyValidationErrors();
    }
}
