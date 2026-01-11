// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Abstractions;

namespace Tests.Abstractions;

public sealed class StringEncryptionOptionsValidatorTests
{
    private static StringEncryptionOptions _CreateValidOptions() => new()
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
    public void should_return_true_when_size_initVectorBytes_length_equal_result_keySize_dividend_on_16()
    {
        // given
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
        settings.InitVectorBytes.Should().HaveCount(20);
        result.IsValid.Should().BeTrue();
    }
}
