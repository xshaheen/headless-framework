// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using FluentValidation.TestHelper;

namespace Tests;

public sealed class UrlValidatorsHttpUrlTests
{
    private readonly TestModelValidator _sut = new();

    private sealed record TestModel(string? HttpUrl);

    private sealed class TestModelValidator : AbstractValidator<TestModel>
    {
        public TestModelValidator() => RuleFor(x => x.HttpUrl).HttpUrl();
    }

    [Fact]
    public void should_not_have_error_when_url_is_null()
    {
        var model = new TestModel(HttpUrl: null);
        var result = _sut.TestValidate(model);
        result.ShouldNotHaveValidationErrorFor(x => x.HttpUrl);
    }

    [Theory]
    [InlineData("http://example.com")]
    [InlineData("https://example.com")]
    public void should_not_have_error_when_url_is_valid(string httpUrl)
    {
        var model = new TestModel(httpUrl);
        var result = _sut.TestValidate(model);
        result.ShouldNotHaveValidationErrorFor(x => x.HttpUrl);
    }

    [Theory]
    [InlineData("invalid-url")]
    [InlineData("ftp://example.com")]
    [InlineData("http:/example.com")]
    [InlineData("://example.com")]
    public void should_have_error_when_url_is_invalid(string httpUrl)
    {
        var model = new TestModel(httpUrl);
        var result = _sut.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.HttpUrl);
    }
}
