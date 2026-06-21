// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Cryptography;
using Headless;
using Headless.Abstractions;

namespace Tests.Abstractions;

public sealed class StringHashOptionsValidatorTests
{
    private static StringHashOptions _CreateValidOptions() =>
        new()
        {
            Iterations = 600_000,
            SizeInBytes = 32,
            Algorithm = HashAlgorithmName.SHA256,
            DefaultSalt = "DefaultSalt",
        };

    [Fact]
    public void should_success_when_valid_settings()
    {
        // given
        var settings = _CreateValidOptions();
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
        var settings = _CreateValidOptions();
        settings.Iterations = 0;
        var validator = new StringHashOptionsValidator();

        // when
        var result = validator.Validate(settings);

        // then
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == nameof(StringHashOptions.Iterations));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    public void should_fail_when_size_is_below_the_minimum(int sizeInBytes)
    {
        // given
        var settings = _CreateValidOptions();
        settings.SizeInBytes = sizeInBytes;
        var validator = new StringHashOptionsValidator();

        // when
        var result = validator.Validate(settings);

        // then
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == nameof(StringHashOptions.SizeInBytes));
    }

    [Fact]
    public void should_fail_when_algorithm_is_default()
    {
        // given
        var settings = _CreateValidOptions();
        settings.Algorithm = default;
        var validator = new StringHashOptionsValidator();

        // when
        var result = validator.Validate(settings);

        // then
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == nameof(StringHashOptions.Algorithm));
    }

    [Fact]
    public void should_fail_when_algorithm_is_a_weak_hash()
    {
        // given (MD5/SHA1 are not permitted for hashing)
        var settings = _CreateValidOptions();
        settings.Algorithm = HashAlgorithmName.SHA1;
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
        var settings = _CreateValidOptions();
        settings.DefaultSalt = null;
        var validator = new StringHashOptionsValidator();

        // when
        var result = validator.Validate(settings);

        // then
        result.IsValid.Should().BeTrue();
    }
}
