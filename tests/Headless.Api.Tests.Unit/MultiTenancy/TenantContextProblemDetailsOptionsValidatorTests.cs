// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.MultiTenancy;
using Headless.Testing.Tests;

namespace Tests.MultiTenancy;

public sealed class TenantContextProblemDetailsOptionsValidatorTests : TestBase
{
    private readonly TenantContextProblemDetailsOptionsValidator _validator = new();

    [Fact]
    public void should_succeed_with_default_options()
    {
        // given
        var options = new TenantContextProblemDetailsOptions();

        // when
        var result = _validator.Validate(options);

        // then
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void should_succeed_with_custom_absolute_uri_prefix()
    {
        // given
        var options = new TenantContextProblemDetailsOptions
        {
            TypeUriPrefix = "https://errors.example.com/tenancy",
            ErrorCode = "tenancy.tenant-required",
        };

        // when
        var result = _validator.Validate(options);

        // then
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void should_succeed_with_trailing_slash_in_uri_prefix()
    {
        // given - factory trims it; validator accepts it
        var options = new TenantContextProblemDetailsOptions { TypeUriPrefix = "https://errors.example.com/tenancy/" };

        // when
        var result = _validator.Validate(options);

        // then
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void should_fail_when_type_uri_prefix_is_blank(string? prefix)
    {
        // given
        var options = new TenantContextProblemDetailsOptions { TypeUriPrefix = prefix! };

        // when
        var result = _validator.Validate(options);

        // then
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(TenantContextProblemDetailsOptions.TypeUriPrefix));
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("relative/path")]
    public void should_fail_when_type_uri_prefix_is_not_absolute(string prefix)
    {
        // given
        var options = new TenantContextProblemDetailsOptions { TypeUriPrefix = prefix };

        // when
        var result = _validator.Validate(options);

        // then
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(TenantContextProblemDetailsOptions.TypeUriPrefix));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void should_fail_when_error_code_is_blank(string? errorCode)
    {
        // given
        var options = new TenantContextProblemDetailsOptions { ErrorCode = errorCode! };

        // when
        var result = _validator.Validate(options);

        // then
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(TenantContextProblemDetailsOptions.ErrorCode));
    }
}
