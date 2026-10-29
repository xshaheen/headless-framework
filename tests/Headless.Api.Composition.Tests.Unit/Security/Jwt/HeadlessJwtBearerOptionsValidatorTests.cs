// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation.TestHelper;
using Headless.Api.Security;
using Headless.Testing.Tests;

namespace Tests.Security.Jwt;

public sealed class HeadlessJwtBearerOptionsValidatorTests : TestBase
{
    private readonly HeadlessJwtBearerOptionsValidator _sut = new();

    [Fact]
    public void should_accept_the_header_only_default()
    {
        _sut.TestValidate(_Valid()).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void should_accept_every_source_with_its_settings()
    {
        var options = _Valid();
        options.TokenSources = JwtTokenSources.Header | JwtTokenSources.Cookie | JwtTokenSources.QueryString;
        options.QueryPathPrefixes = ["/hubs", "/events"];

        _sut.TestValidate(options).ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(JwtTokenSources.None)]
    [InlineData((JwtTokenSources)8)]
    public void should_reject_no_source_or_an_unknown_one(JwtTokenSources sources)
    {
        var options = _Valid();
        options.TokenSources = sources;

        _sut.TestValidate(options).ShouldHaveValidationErrorFor(x => x.TokenSources);
    }

    [Theory]
    [InlineData("")]
    [InlineData("access token")]
    [InlineData("access;token")]
    public void should_reject_an_invalid_cookie_name_when_the_cookie_source_is_on(string name)
    {
        var options = _Valid();
        options.TokenSources = JwtTokenSources.Cookie;
        options.CookieName = name;

        _sut.TestValidate(options).ShouldHaveValidationErrorFor(x => x.CookieName);
    }

    [Fact]
    public void should_ignore_the_cookie_name_when_the_cookie_source_is_off()
    {
        var options = _Valid();
        options.CookieName = "";

        _sut.TestValidate(options).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void should_require_a_path_prefix_for_the_query_string_source()
    {
        var options = _Valid();
        options.TokenSources = JwtTokenSources.QueryString;

        _sut.TestValidate(options)
            .ShouldHaveValidationErrorFor(x => x.QueryPathPrefixes)
            .WithErrorMessage(
                "QueryPathPrefixes needs at least one path, such as '/hubs', when TokenSources includes QueryString:"
                    + " a token in a URL leaks into logs, so it is read only where a header cannot be sent."
            );
    }

    [Fact]
    public void should_require_a_query_parameter_name_for_the_query_string_source()
    {
        var options = _Valid();
        options.TokenSources = JwtTokenSources.QueryString;
        options.QueryPathPrefixes = ["/hubs"];
        options.QueryParameterName = "";

        _sut.TestValidate(options).ShouldHaveValidationErrorFor(x => x.QueryParameterName);
    }

    [Theory]
    [InlineData("hubs")]
    [InlineData("/")]
    [InlineData("")]
    public void should_reject_a_path_prefix_that_does_not_name_a_rooted_path(string prefix)
    {
        var options = _Valid();
        options.TokenSources = JwtTokenSources.QueryString;
        options.QueryPathPrefixes = [prefix];

        _sut.TestValidate(options)
            .Errors.Should()
            .ContainSingle()
            .Which.ErrorMessage.Should()
            .Contain("start with '/'");
    }

    private static HeadlessJwtBearerOptions _Valid()
    {
        return new HeadlessJwtBearerOptions
        {
            SigningKey = "this-is-a-test-signing-key-must-be-32-bytes-or-more",
            Issuer = "issuer",
            Audience = "audience",
        };
    }
}
