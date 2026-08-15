// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation.TestHelper;
using Headless.MultiTenancy;

namespace Tests;

public sealed class TenantCatalogOptionsValidatorTests
{
    private readonly TenantCatalogOptionsValidator _sut = new();

    [Fact]
    public void should_accept_default_options()
    {
        // given
        var options = new TenantCatalogOptions();

        // when
        var result = _sut.TestValidate(options);

        // then
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void should_reject_non_positive_cache_expiration(int seconds)
    {
        // given
        var options = new TenantCatalogOptions { CacheExpiration = TimeSpan.FromSeconds(seconds) };

        // when
        var result = _sut.TestValidate(options);

        // then
        result.ShouldHaveValidationErrorFor(x => x.CacheExpiration);
    }

    [Fact]
    public void should_reject_negative_unknown_identifier_cache_expiration()
    {
        // given
        var options = new TenantCatalogOptions { UnknownIdentifierCacheExpiration = TimeSpan.FromSeconds(-1) };

        // when
        var result = _sut.TestValidate(options);

        // then
        result.ShouldHaveValidationErrorFor(x => x.UnknownIdentifierCacheExpiration);
    }

    [Fact]
    public void should_accept_zero_unknown_identifier_cache_expiration()
    {
        // given — zero disables negative caching (R12); it must not fail validation.
        var options = new TenantCatalogOptions { UnknownIdentifierCacheExpiration = TimeSpan.Zero };

        // when
        var result = _sut.TestValidate(options);

        // then
        result.ShouldNotHaveValidationErrorFor(x => x.UnknownIdentifierCacheExpiration);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void should_reject_zero_or_negative_max_identifier_length(int length)
    {
        // given
        var options = new TenantCatalogOptions { MaxIdentifierLength = length };

        // when
        var result = _sut.TestValidate(options);

        // then
        result.ShouldHaveValidationErrorFor(x => x.MaxIdentifierLength);
    }

    [Fact]
    public void should_reject_null_identifier_pattern()
    {
        // given
        var options = new TenantCatalogOptions { IdentifierPattern = null! };

        // when
        var result = _sut.TestValidate(options);

        // then
        result.ShouldHaveValidationErrorFor(x => x.IdentifierPattern);
    }

    [Fact]
    public void should_reject_blank_ignored_identifier_entries()
    {
        // given
        var options = new TenantCatalogOptions { IgnoredIdentifiers = ["www", " ", "api"] };

        // when
        var result = _sut.TestValidate(options);

        // then
        result.ShouldHaveValidationErrorFor("IgnoredIdentifiers[1]");
    }
}
