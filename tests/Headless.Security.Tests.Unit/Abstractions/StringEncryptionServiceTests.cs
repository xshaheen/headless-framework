// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Cryptography;
using Headless;
using Headless.Abstractions;

namespace Tests.Abstractions;

public sealed class StringEncryptionServiceTests
{
    private static StringEncryptionOptions _CreateValidOptions()
    {
        return new()
        {
            DefaultPassPhrase = "TestPassPhrase123456",
            DefaultSalt = "TestSalt12345678"u8.ToArray(),
            KeySize = 256,
        };
    }

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
    public void should_produce_different_ciphertext_for_same_plaintext_due_to_random_nonce()
    {
        // given
        var sut = new StringEncryptionService(_CreateValidOptions());
        const string plainText = "RepeatedMessage";

        // when
        var first = sut.Encrypt(plainText);
        var second = sut.Encrypt(plainText);

        // then (a fresh nonce per call means identical plaintexts never share ciphertext)
        first.Should().NotBe(second);
        sut.Decrypt(first).Should().Be(plainText);
        sut.Decrypt(second).Should().Be(plainText);
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
        var decryptedWithCustom = sut.Decrypt(encryptedWithCustom, passPhrase: customPassPhrase);

        // then
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
        var decryptedWithCustom = sut.Decrypt(encryptedWithCustom, salt: customSalt);

        // then
        decryptedWithCustom.Should().Be(plainText);
    }

    [Fact]
    public void should_throw_when_decrypting_with_wrong_pass_phrase()
    {
        // given
        var sut = new StringEncryptionService(_CreateValidOptions());
        var encrypted = sut.Encrypt("SecretMessage", passPhrase: "FirstPassPhrase12345");

        // when
        var act = () => sut.Decrypt(encrypted, passPhrase: "SecondPassPhrase1234");

        // then (AES-GCM authentication rejects a key that did not produce the tag)
        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void should_throw_when_cipher_text_is_tampered()
    {
        // given
        var sut = new StringEncryptionService(_CreateValidOptions());
        var encrypted = sut.Encrypt("SensitiveData")!;
        var bytes = Convert.FromBase64String(encrypted);
        bytes[^1] ^= 0xFF; // flip the last byte of the cipher text
        var tampered = Convert.ToBase64String(bytes);

        // when
        var act = () => sut.Decrypt(tampered);

        // then
        act.Should().Throw<CryptographicException>();
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
