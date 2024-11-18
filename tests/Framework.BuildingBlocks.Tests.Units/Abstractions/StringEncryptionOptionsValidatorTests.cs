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
}
