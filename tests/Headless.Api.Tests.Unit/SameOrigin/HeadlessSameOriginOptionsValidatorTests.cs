// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation.TestHelper;
using Headless.Api;
using Headless.Testing.Tests;

namespace Tests.SameOrigin;

public sealed class HeadlessSameOriginOptionsValidatorTests : TestBase
{
    private readonly HeadlessSameOriginOptionsValidator _sut = new();

    [Fact]
    public void should_accept_the_defaults()
    {
        _sut.TestValidate(new HeadlessSameOriginOptions()).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void should_accept_http_and_https_origins_and_a_token_header_name()
    {
        var options = new HeadlessSameOriginOptions
        {
            AllowedOrigins = ["https://app.example.com", "http://localhost:5173"],
            RequiredHeaderName = "X-Requested-With",
        };

        _sut.TestValidate(options).ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("", "empty")]
    [InlineData("https://app.example.com/", "trailing slash")]
    [InlineData("https://app.example.com/spa", "path")]
    [InlineData("app.example.com", "absolute origin")]
    [InlineData("https://app.example.com:443", "default port")]
    [InlineData("capacitor://localhost", "http or https")]
    [InlineData("ftp://files.example.com", "http or https")]
    public void should_reject_an_origin_that_is_not_a_bare_http_origin(string origin, string reason)
    {
        var options = new HeadlessSameOriginOptions { AllowedOrigins = [origin] };

        var result = _sut.TestValidate(options);

        result.Errors.Should().ContainSingle().Which.ErrorMessage.Should().Contain(reason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("X Requested With")]
    [InlineData("X-Requested:With")]
    public void should_reject_a_header_name_that_is_not_a_token(string name)
    {
        var options = new HeadlessSameOriginOptions { RequiredHeaderName = name };

        _sut.TestValidate(options).ShouldHaveValidationErrorFor(x => x.RequiredHeaderName);
    }
}
