// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Kernel.BuildingBlocks.Abstractions;

namespace Tests.Abstractions;

public sealed class StringEncryptionSettingsTests
{
    [Fact]
    public void should_success_when_default_settings()
    {
        // given
        var defaultSettings = new StringEncryptionSettings();
        var encryptionService = new StringEncryptionService(defaultSettings);

        // when
        var encryptedText = encryptionService.Encrypt("Hello World");
        var decryptedText = encryptionService.Decrypt(encryptedText);

        // then
        decryptedText.Should().Be("Hello World");
    }
}
