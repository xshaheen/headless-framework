// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation.TestHelper;
using Headless.MultiTenancy;

namespace Tests;

public sealed class ConfigurationTenantStoreOptionsValidatorTests
{
    private readonly ConfigurationTenantStoreOptionsValidator _sut = new();

    [Fact]
    public void should_accept_default_options()
    {
        // given
        var options = new ConfigurationTenantStoreOptions();

        // when
        var result = _sut.TestValidate(options);

        // then
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void should_accept_a_well_formed_seed()
    {
        // given
        var options = new ConfigurationTenantStoreOptions
        {
            Tenants =
            [
                new ConfigurationTenantSeed
                {
                    Id = "ten_1",
                    Identifier = "acme",
                    Name = "Acme",
                },
            ],
        };

        // when
        var result = _sut.TestValidate(options);

        // then
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void should_reject_null_tenants()
    {
        // given
        var options = new ConfigurationTenantStoreOptions { Tenants = null! };

        // when
        var result = _sut.TestValidate(options);

        // then
        result.ShouldHaveValidationErrorFor(x => x.Tenants);
    }

    [Fact]
    public void should_reject_empty_seed_id()
    {
        // given
        var options = new ConfigurationTenantStoreOptions
        {
            Tenants = [new ConfigurationTenantSeed { Id = "", Identifier = "acme" }],
        };

        // when
        var result = _sut.TestValidate(options);

        // then
        result.ShouldHaveValidationErrorFor("Tenants[0].Id");
    }

    [Fact]
    public void should_reject_empty_seed_identifier()
    {
        // given
        var options = new ConfigurationTenantStoreOptions
        {
            Tenants = [new ConfigurationTenantSeed { Id = "ten_1", Identifier = "" }],
        };

        // when
        var result = _sut.TestValidate(options);

        // then
        result.ShouldHaveValidationErrorFor("Tenants[0].Identifier");
    }

    [Theory]
    [InlineData("ac_me")]
    [InlineData("ac me")]
    [InlineData("ac!me")]
    public void should_reject_seed_identifier_with_characters_outside_the_default_shape(string identifier)
    {
        // given
        var options = new ConfigurationTenantStoreOptions
        {
            Tenants = [new ConfigurationTenantSeed { Id = "ten_1", Identifier = identifier }],
        };

        // when
        var result = _sut.TestValidate(options);

        // then
        result.ShouldHaveValidationErrorFor("Tenants[0].Identifier");
    }

    [Fact]
    public void should_reject_seed_identifier_exceeding_the_default_max_length()
    {
        // given — 64 chars exceeds the DNS-label default (63)
        var identifier = new string('a', 64);
        var options = new ConfigurationTenantStoreOptions
        {
            Tenants = [new ConfigurationTenantSeed { Id = "ten_1", Identifier = identifier }],
        };

        // when
        var result = _sut.TestValidate(options);

        // then
        result.ShouldHaveValidationErrorFor("Tenants[0].Identifier");
    }

    [Fact]
    public void should_reject_duplicate_normalized_identifiers()
    {
        // given — AE10 configuration arm: "Acme" and " acme " both normalize to "acme"
        var options = new ConfigurationTenantStoreOptions
        {
            Tenants =
            [
                new ConfigurationTenantSeed { Id = "ten_1", Identifier = "Acme" },
                new ConfigurationTenantSeed { Id = "ten_2", Identifier = " acme " },
            ],
        };

        // when
        var result = _sut.TestValidate(options);

        // then
        result.ShouldHaveValidationErrorFor(x => x.Tenants);
        result
            .Errors.Should()
            .Contain(error =>
                error.ErrorMessage.Contains("normalize to the same identifier", StringComparison.Ordinal)
            );
    }

    [Fact]
    public void should_reject_duplicate_tenant_ids()
    {
        // given
        var options = new ConfigurationTenantStoreOptions
        {
            Tenants =
            [
                new ConfigurationTenantSeed { Id = "ten_1", Identifier = "acme" },
                new ConfigurationTenantSeed { Id = "ten_1", Identifier = "globex" },
            ],
        };

        // when
        var result = _sut.TestValidate(options);

        // then
        result.ShouldHaveValidationErrorFor(x => x.Tenants);
        result
            .Errors.Should()
            .Contain(error =>
                error.ErrorMessage.Contains("share the same canonical tenant id", StringComparison.Ordinal)
            );
    }
}
