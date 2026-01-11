// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Abstractions;

namespace Tests.Abstractions;

public sealed class StringEncryptionOptionsTests
{
    private static StringEncryptionOptions _CreateTestOptions() => new()
    {
        DefaultPassPhrase = "TestPassPhrase123456",
        InitVectorBytes = "TestIV0123456789"u8.ToArray(),
        DefaultSalt = "TestSalt"u8.ToArray(),
    };

    [Fact]
    public void should_success_when_valid_settings()
    {
        // given
        var settings = _CreateTestOptions();
        var encryptionService = new StringEncryptionService(settings);

        // when
        var encryptedText = encryptionService.Encrypt("Hello World");
        var decryptedText = encryptionService.Decrypt(encryptedText);

        // then
        decryptedText.Should().Be("Hello World");
    }
}
