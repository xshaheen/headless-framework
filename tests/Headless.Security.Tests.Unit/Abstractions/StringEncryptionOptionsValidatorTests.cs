// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Security;

namespace Tests.Abstractions;

public sealed class StringEncryptionOptionsValidatorTests
{
    private static StringEncryptionOptions _CreateValidOptions()
    {
        return new() { DefaultPassPhrase = "TestPassPhrase123456", DefaultSalt = "TestSalt"u8.ToArray() };
    }

    [Theory]
    [InlineData(128)]
    [InlineData(192)]
    [InlineData(256)]
    public void should_succeed_for_every_legal_aes_key_size(int keySize)
    {
        // given
        var settings = _CreateValidOptions();
        settings.KeySize = keySize;
        var validator = new StringEncryptionOptionsValidator();

        // when
        var result = validator.Validate(settings);

        // then
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void should_fail_when_pass_phrase_is_empty()
    {
        // given
        var settings = _CreateValidOptions();
        settings.DefaultPassPhrase = string.Empty;
        var validator = new StringEncryptionOptionsValidator();

        // when
        var result = validator.Validate(settings);

        // then
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(StringEncryptionOptions.DefaultPassPhrase));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(320)]
    public void should_fail_when_key_size_is_not_a_legal_aes_size(int keySize)
    {
        // given
        var settings = _CreateValidOptions();
        settings.KeySize = keySize;
        var validator = new StringEncryptionOptionsValidator();

        // when
        var result = validator.Validate(settings);

        // then
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(StringEncryptionOptions.KeySize));
    }

    [Fact]
    public void should_fail_when_iterations_are_zero()
    {
        // given
        var settings = _CreateValidOptions();
        settings.Iterations = 0;
        var validator = new StringEncryptionOptionsValidator();

        // when
        var result = validator.Validate(settings);

        // then
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(StringEncryptionOptions.Iterations));
    }

    [Fact]
    public void should_fail_when_salt_is_empty()
    {
        // given
        var settings = _CreateValidOptions();
        settings.DefaultSalt = [];
        var validator = new StringEncryptionOptionsValidator();

        // when
        var result = validator.Validate(settings);

        // then
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(StringEncryptionOptions.DefaultSalt));
    }
}
