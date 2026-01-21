// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using FluentValidation.TestHelper;

namespace Tests;

public sealed class UrlValidatorsUrlTests
{
    private readonly TestModelValidator _sut = new();

    private sealed record TestModel(string? Url);

    private sealed class TestModelValidator : AbstractValidator<TestModel>
    {
        public TestModelValidator() => RuleFor(x => x.Url).Url();
    }

    [Fact]
    public void should_not_have_error_when_url_is_null()
    {
        var model = new TestModel(Url: null);
        var result = _sut.TestValidate(model);
        result.ShouldNotHaveValidationErrorFor(x => x.Url);
    }

    [Theory]
    [InlineData("http://example.com")]
    [InlineData("https://example.com")]
    [InlineData("ftp://example.com")]
    public void should_not_have_error_when_url_is_valid(string url)
    {
        var model = new TestModel(url);
        var result = _sut.TestValidate(model);
        result.ShouldNotHaveValidationErrorFor(x => x.Url);
    }

    [Theory]
    [InlineData("invalid-url")]
    [InlineData("http:/example.com")]
    [InlineData("://example.com")]
    public void should_have_error_when_url_is_invalid(string url)
    {
        var model = new TestModel(url);
        var result = _sut.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.Url);
    }
}
