// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;

namespace Tests.Abstractions;

public sealed class StringEncryptionServiceTests
{
    private static StringEncryptionOptions _CreateValidOptions() =>
        new()
        {
            DefaultPassPhrase = "TestPassPhrase123456",
            InitVectorBytes = "TestIV0123456789"u8.ToArray(),
            DefaultSalt = "TestSalt12345678"u8.ToArray(),
            KeySize = 256,
        };

    [Fact]
    public void should_return_null_when_plain_text_is_null()
    {
        // given
        var sut = new StringEncryptionService(_CreateValidOptions());

        // when
        var result = sut.Encrypt(null);

        // then
        result.Should().BeNull();
    }

    [Fact]
    public void should_return_null_when_cipher_text_is_null()
    {
        // given
        var sut = new StringEncryptionService(_CreateValidOptions());

        // when
        var result = sut.Decrypt(null);

        // then
        result.Should().BeNull();
    }

    [Fact]
    public void should_return_null_when_cipher_text_is_empty()
    {
        // given
        var sut = new StringEncryptionService(_CreateValidOptions());

        // when
        var result = sut.Decrypt("");

        // then
        result.Should().BeNull();
    }

    [Fact]
    public void should_encrypt_and_decrypt_round_trip()
    {
        // given
        var sut = new StringEncryptionService(_CreateValidOptions());
        const string plainText = "Hello, World! 日本語 🔐";

        // when
        var encrypted = sut.Encrypt(plainText);
        var decrypted = sut.Decrypt(encrypted);

        // then
        encrypted.Should().NotBeNullOrEmpty();
        encrypted.Should().NotBe(plainText);
        decrypted.Should().Be(plainText);
    }

    [Fact]
    public void should_use_custom_pass_phrase()
    {
        // given
        var options = _CreateValidOptions();
        var sut = new StringEncryptionService(options);
        const string plainText = "SecretMessage";
        const string customPassPhrase = "CustomPassPhrase12345";

        // when
        var encryptedWithCustom = sut.Encrypt(plainText, passPhrase: customPassPhrase);
        var encryptedWithDefault = sut.Encrypt(plainText);
        var decryptedWithCustom = sut.Decrypt(encryptedWithCustom, passPhrase: customPassPhrase);

        // then
        encryptedWithCustom.Should().NotBe(encryptedWithDefault);
        decryptedWithCustom.Should().Be(plainText);
    }

    [Fact]
    public void should_use_custom_salt()
    {
        // given
        var options = _CreateValidOptions();
        var sut = new StringEncryptionService(options);
        const string plainText = "SecretMessage";
        var customSalt = "CustomSalt123456"u8.ToArray();

        // when
        var encryptedWithCustom = sut.Encrypt(plainText, salt: customSalt);
        var encryptedWithDefault = sut.Encrypt(plainText);
        var decryptedWithCustom = sut.Decrypt(encryptedWithCustom, salt: customSalt);

        // then
        encryptedWithCustom.Should().NotBe(encryptedWithDefault);
        decryptedWithCustom.Should().Be(plainText);
    }

    [Fact]
    public void should_produce_different_output_for_different_passphrases()
    {
        // given
        var options = _CreateValidOptions();
        var sut = new StringEncryptionService(options);
        const string plainText = "SameMessage";
        const string passPhrase1 = "FirstPassPhrase12345";
        const string passPhrase2 = "SecondPassPhrase1234";

        // when
        var encrypted1 = sut.Encrypt(plainText, passPhrase: passPhrase1);
        var encrypted2 = sut.Encrypt(plainText, passPhrase: passPhrase2);

        // then
        encrypted1.Should().NotBe(encrypted2);
    }

    [Fact]
    public void should_throw_on_invalid_base64_cipher_text()
    {
        // given
        var sut = new StringEncryptionService(_CreateValidOptions());
        const string invalidBase64 = "not-valid-base64!!!";

        // when
        var act = () => sut.Decrypt(invalidBase64);

        // then
        act.Should().Throw<FormatException>();
    }
}
