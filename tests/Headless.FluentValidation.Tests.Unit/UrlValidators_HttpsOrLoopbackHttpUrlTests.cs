// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using FluentValidation.TestHelper;

namespace Tests;

public sealed class UrlValidatorsHttpsOrLoopbackHttpUrlTests
{
    private readonly TestModelValidator _sut = new();

    private sealed record TestModel(string? Endpoint);

    private sealed class TestModelValidator : AbstractValidator<TestModel>
    {
        public TestModelValidator() => RuleFor(x => x.Endpoint).HttpsOrLoopbackHttpUrl();
    }

    [Fact]
    public void should_not_have_error_when_url_is_null()
    {
        var result = _sut.TestValidate(new TestModel(Endpoint: null));

        result.ShouldNotHaveValidationErrorFor(x => x.Endpoint);
    }

    [Theory]
    [InlineData("https://example.com/api")]
    [InlineData("https://10.0.0.10/api")]
    [InlineData("http://localhost:5000/api")]
    [InlineData("http://127.0.0.1:5000/api")]
    [InlineData("http://[::1]:5000/api")]
    public void should_not_have_error_when_url_uses_secure_transport_or_loopback_http(string endpoint)
    {
        var result = _sut.TestValidate(new TestModel(endpoint));

        result.ShouldNotHaveValidationErrorFor(x => x.Endpoint);
    }

    [Theory]
    [InlineData("http://example.com/api")]
    [InlineData("http://10.0.0.10/api")]
    [InlineData("http://internal-api/api")]
    [InlineData("https://user:password@example.com/api")]
    [InlineData("http://user:password@localhost:5000/api")]
    [InlineData("not-a-url")]
    [InlineData("ftp://example.com/api")]
    public void should_have_error_when_url_uses_remote_http_userinfo_or_an_unsupported_scheme(string endpoint)
    {
        var result = _sut.TestValidate(new TestModel(endpoint));

        result.ShouldHaveValidationErrorFor(x => x.Endpoint);
    }
}
