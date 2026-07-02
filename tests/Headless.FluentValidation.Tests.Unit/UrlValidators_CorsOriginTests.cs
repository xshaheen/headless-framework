// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using FluentValidation.TestHelper;

namespace Tests;

public sealed class UrlValidatorsCorsOriginTests
{
    private readonly TestModelValidator _sut = new();

    private sealed record TestModel(string? Origin);

    private sealed class TestModelValidator : AbstractValidator<TestModel>
    {
        public TestModelValidator() => RuleFor(x => x.Origin).CorsOrigin();
    }

    [Fact]
    public void should_not_have_error_when_origin_is_null()
    {
        var result = _sut.TestValidate(new TestModel(Origin: null));
        result.ShouldNotHaveValidationErrorFor(x => x.Origin);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("http://example.com")]
    [InlineData("https://example.com")]
    [InlineData("https://example.com:8443")]
    public void should_not_have_error_when_origin_is_valid(string origin)
    {
        var result = _sut.TestValidate(new TestModel(origin));
        result.ShouldNotHaveValidationErrorFor(x => x.Origin);
    }

    [Theory]
    [InlineData("not-an-origin")]
    [InlineData("://example.com")]
    public void should_report_invalid_format_for_non_uri(string origin)
    {
        var result = _sut.TestValidate(new TestModel(origin));
        result.ShouldHaveValidationErrorFor(x => x.Origin).WithErrorCode("url:invalid_origin_format");
    }

    // Regression: a non-http(s) absolute scheme previously threw NullReferenceException while
    // writing the {Scheme} placeholder onto a freshly-constructed ValidationFailure.
    [Theory]
    [InlineData("ftp://example.com")]
    [InlineData("ws://example.com")]
    [InlineData("file://example.com")]
    public void should_report_invalid_scheme_for_non_http_scheme(string origin)
    {
        var result = _sut.TestValidate(new TestModel(origin));
        result.ShouldHaveValidationErrorFor(x => x.Origin).WithErrorCode("url:invalid_origin_scheme");
    }

    // Regression: an Origin (RFC 6454) is scheme://host[:port] only and must not carry userinfo.
    [Theory]
    [InlineData("http://user@example.com")]
    [InlineData("http://user:pass@example.com")]
    public void should_report_invalid_format_for_userinfo(string origin)
    {
        var result = _sut.TestValidate(new TestModel(origin));
        result.ShouldHaveValidationErrorFor(x => x.Origin).WithErrorCode("url:invalid_origin_format");
    }

    [Theory]
    [InlineData("http://example.com/path")]
    [InlineData("http://example.com?query=1")]
    [InlineData("http://example.com#fragment")]
    public void should_report_invalid_path_for_non_root(string origin)
    {
        var result = _sut.TestValidate(new TestModel(origin));
        result.ShouldHaveValidationErrorFor(x => x.Origin).WithErrorCode("url:invalid_origin_path");
    }

    [Fact]
    public void should_report_trailing_slash()
    {
        var result = _sut.TestValidate(new TestModel("http://example.com/"));
        result.ShouldHaveValidationErrorFor(x => x.Origin).WithErrorCode("url:invalid_origin_trailing_slash");
    }

    // Uri.TryCreate trims surrounding whitespace, so a padded origin would otherwise validate but never
    // match a real (ordinal-compared) Origin header.
    [Theory]
    [InlineData(" http://example.com")]
    [InlineData("http://example.com ")]
    [InlineData("http://example.com\t")]
    public void should_report_invalid_format_for_whitespace_padding(string origin)
    {
        var result = _sut.TestValidate(new TestModel(origin));
        result.ShouldHaveValidationErrorFor(x => x.Origin).WithErrorCode("url:invalid_origin_format");
    }

    [Fact]
    public void should_substitute_placeholders_in_failure_messages()
    {
        var formatFailure = _sut.TestValidate(new TestModel("not-an-origin"))
            .Errors.Single(e => string.Equals(e.PropertyName, nameof(TestModel.Origin), StringComparison.Ordinal));
        formatFailure.ErrorMessage.Should().Contain("not-an-origin").And.NotContain("{PropertyValue}");

        var schemeFailure = _sut.TestValidate(new TestModel("ftp://example.com"))
            .Errors.Single(e => string.Equals(e.PropertyName, nameof(TestModel.Origin), StringComparison.Ordinal));
        schemeFailure.ErrorMessage.Should().Contain("ftp").And.NotContain("{Scheme}");
    }
}
