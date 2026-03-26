// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Cryptography;
using Headless.Abstractions;

namespace Tests.Abstractions;

public sealed class StringHashOptionsValidatorTests
{
    [Fact]
    public void should_success_when_valid_settings()
    {
        // given
        var settings = new StringHashOptions
        {
            Iterations = 600_000,
            Size = 128,
            Algorithm = HashAlgorithmName.SHA256,
            DefaultSalt = "DefaultSalt",
        };

        var validator = new StringHashOptionsValidator();

        // when
        var result = validator.Validate(settings);

        // then
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void should_fail_when_iterations_are_zero()
    {
        // given
        var settings = new StringHashOptions
        {
            Iterations = 0,
            Size = 128,
            Algorithm = HashAlgorithmName.SHA256,
            DefaultSalt = "DefaultSalt",
        };

        var validator = new StringHashOptionsValidator();

        // when
        var result = validator.Validate(settings);

        // then
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == nameof(StringHashOptions.Iterations));
    }

    [Fact]
    public void should_fail_when_size_is_zero()
    {
        // given
        var settings = new StringHashOptions
        {
            Iterations = 600_000,
            Size = 0,
            Algorithm = HashAlgorithmName.SHA256,
            DefaultSalt = "DefaultSalt",
        };

        var validator = new StringHashOptionsValidator();

        // when
        var result = validator.Validate(settings);

        // then
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == nameof(StringHashOptions.Size));
    }

    [Fact]
    public void should_fail_when_algorithm_is_default()
    {
        // given
        var settings = new StringHashOptions
        {
            Iterations = 600_000,
            Size = 128,
            Algorithm = default,
            DefaultSalt = "DefaultSalt",
        };

        var validator = new StringHashOptionsValidator();

        // when
        var result = validator.Validate(settings);

        // then
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == nameof(StringHashOptions.Algorithm));
    }

    [Fact]
    public void should_success_when_default_salt_is_missing()
    {
        // given
        var settings = new StringHashOptions
        {
            Iterations = 600_000,
            Size = 128,
            Algorithm = HashAlgorithmName.SHA256,
        };

        var validator = new StringHashOptionsValidator();

        // when
        var result = validator.Validate(settings);

        // then
        result.IsValid.Should().BeTrue();
    }
}
