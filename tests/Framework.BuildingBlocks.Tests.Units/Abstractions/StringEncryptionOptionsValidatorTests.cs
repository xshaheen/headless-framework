// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.BuildingBlocks.Abstractions;

namespace Tests.Abstractions;

public sealed class StringEncryptionOptionsValidatorTests
{
    [Fact]
    public void should_success_when_default_settings()
    {
        // given
        var defaultSettings = new StringEncryptionOptions();
        var validator = new StringEncryptionOptionsValidator();

        // when
        var result = validator.Validate(defaultSettings);

        // then
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void should_fail_when_set_property_as_empty_settings()
    {
        // given
        var defaultSettings = new StringEncryptionOptions { DefaultPassPhrase = string.Empty };

        var validator = new StringEncryptionOptionsValidator();

        // when
        var result = validator.Validate(defaultSettings);

        // then
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void should_fail_when_set_property_keySize_by_zero_settings()
    {
        // given
        var defaultSettings = new StringEncryptionOptions
        {
            KeySize = 0,
            DefaultPassPhrase = "SHAHkLaXNOGZ044IM8",
            InitVectorBytes = "shE49230Tf093b42"u8.ToArray(),
            DefaultSalt = "hgt!16kl"u8.ToArray(),
        };

        var validator = new StringEncryptionOptionsValidator();

        // when
        var result = validator.Validate(defaultSettings);

        // then
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void should_return_true_when_size_initVectorBytes_length_equal_result_keySize_dividend_on_16()
    {
        // given
        var defaultSettings = new StringEncryptionOptions
        {
            KeySize = 320,
            DefaultPassPhrase = "SHAHkLaXNOGZ044IM8",
            InitVectorBytes = "shE49230Tf093b421723"u8.ToArray(),
            DefaultSalt = "hgt!16kl"u8.ToArray(),
        };

        var validator = new StringEncryptionOptionsValidator();

        // when
        var result = validator.Validate(defaultSettings);

        // then
        defaultSettings.InitVectorBytes.Should().HaveCount(20);
        result.IsValid.Should().BeTrue();
    }
}
