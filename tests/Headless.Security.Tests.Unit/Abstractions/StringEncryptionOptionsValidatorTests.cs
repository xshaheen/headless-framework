// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;

namespace Tests.Abstractions;

public sealed class StringEncryptionOptionsValidatorTests
{
    private static StringEncryptionOptions _CreateValidOptions() =>
        new()
        {
            DefaultPassPhrase = "TestPassPhrase123456",
            InitVectorBytes = "TestIV0123456789"u8.ToArray(),
            DefaultSalt = "TestSalt"u8.ToArray(),
        };

    [Fact]
    public void should_success_when_valid_settings()
    {
        // given
        var settings = _CreateValidOptions();
        var validator = new StringEncryptionOptionsValidator();

        // when
        var result = validator.Validate(settings);

        // then
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void should_fail_when_set_property_as_empty_settings()
    {
        // given
        var settings = new StringEncryptionOptions
        {
            DefaultPassPhrase = string.Empty,
            InitVectorBytes = "TestIV0123456789"u8.ToArray(),
            DefaultSalt = "TestSalt"u8.ToArray(),
        };

        var validator = new StringEncryptionOptionsValidator();

        // when
        var result = validator.Validate(settings);

        // then
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void should_fail_when_set_property_keySize_by_zero_settings()
    {
        // given
        var settings = new StringEncryptionOptions
        {
            KeySize = 0,
            DefaultPassPhrase = "TestPassPhrase123456",
            InitVectorBytes = "TestIV0123456789"u8.ToArray(),
            DefaultSalt = "TestSalt"u8.ToArray(),
        };

        var validator = new StringEncryptionOptionsValidator();

        // when
        var result = validator.Validate(settings);

        // then
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void should_fail_when_key_size_is_not_a_legal_aes_size()
    {
        // given (320 is not a legal AES key size; the IV is sized to the old, wrong KeySize/16 formula)
        var settings = new StringEncryptionOptions
        {
            KeySize = 320,
            DefaultPassPhrase = "TestPassPhrase123456",
            InitVectorBytes = "shE49230Tf093b421723"u8.ToArray(),
            DefaultSalt = "TestSalt"u8.ToArray(),
        };

        var validator = new StringEncryptionOptionsValidator();

        // when
        var result = validator.Validate(settings);

        // then
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(StringEncryptionOptions.KeySize));
    }

    [Theory]
    [InlineData(128)]
    [InlineData(192)]
    [InlineData(256)]
    public void should_require_a_16_byte_iv_for_every_legal_aes_key_size(int keySize)
    {
        // given (AES always uses a 16-byte IV regardless of key size)
        var settings = new StringEncryptionOptions
        {
            KeySize = keySize,
            DefaultPassPhrase = "TestPassPhrase123456",
            InitVectorBytes = "TestIV0123456789"u8.ToArray(),
            DefaultSalt = "TestSalt"u8.ToArray(),
        };

        var validator = new StringEncryptionOptionsValidator();

        // when
        var result = validator.Validate(settings);

        // then
        settings.InitVectorBytes.Should().HaveCount(16);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void should_fail_when_key_size_is_negative()
    {
        // given
        var settings = new StringEncryptionOptions
        {
            KeySize = -1,
            DefaultPassPhrase = "TestPassPhrase123456",
            InitVectorBytes = "TestIV0123456789"u8.ToArray(),
            DefaultSalt = "TestSalt"u8.ToArray(),
        };

        var validator = new StringEncryptionOptionsValidator();

        // when
        var result = validator.Validate(settings);

        // then
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(StringEncryptionOptions.KeySize));
    }

    [Fact]
    public void should_fail_when_init_vector_is_empty()
    {
        // given
        var settings = new StringEncryptionOptions
        {
            DefaultPassPhrase = "TestPassPhrase123456",
            InitVectorBytes = [],
            DefaultSalt = "TestSalt"u8.ToArray(),
        };

        var validator = new StringEncryptionOptionsValidator();

        // when
        var result = validator.Validate(settings);

        // then
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(StringEncryptionOptions.InitVectorBytes));
    }

    [Fact]
    public void should_fail_when_init_vector_length_mismatch()
    {
        // given
        var settings = new StringEncryptionOptions
        {
            KeySize = 256,
            DefaultPassPhrase = "TestPassPhrase123456",
            InitVectorBytes = "TooShort"u8.ToArray(),
            DefaultSalt = "TestSalt"u8.ToArray(),
        };

        var validator = new StringEncryptionOptionsValidator();

        // when
        var result = validator.Validate(settings);

        // then
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(StringEncryptionOptions.InitVectorBytes));
    }

    [Fact]
    public void should_fail_when_salt_is_empty()
    {
        // given
        var settings = new StringEncryptionOptions
        {
            DefaultPassPhrase = "TestPassPhrase123456",
            InitVectorBytes = "TestIV0123456789"u8.ToArray(),
            DefaultSalt = [],
        };

        var validator = new StringEncryptionOptionsValidator();

        // when
        var result = validator.Validate(settings);

        // then
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(StringEncryptionOptions.DefaultSalt));
    }
}
