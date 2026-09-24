// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Security;

namespace Tests.SecretHashing;

public sealed class SecretHasherTests
{
    [Fact]
    public void should_produce_a_different_encoding_on_every_call_and_verify_each()
    {
        // given
        var sut = SecretHashingTestKit.CreateHasher(SecretHashingTestKit.Pbkdf2Options());

        // when
        var first = sut.Hash("123456");
        var second = sut.Hash("123456");

        // then
        first.Should().NotBe(second);
        sut.Verify("123456", first).Should().Be(new SecretVerification(true, null));
        sut.Verify("123456", second).Should().Be(new SecretVerification(true, null));
    }

    [Fact]
    public void should_encode_pbkdf2_as_a_phc_string_with_configured_parameters()
    {
        // given
        var sut = SecretHashingTestKit.CreateHasher(SecretHashingTestKit.Pbkdf2Options());

        // when
        var encoded = sut.Hash("secret");

        // then
        encoded.Should().StartWith($"$pbkdf2-sha256$i={SecretHashingTestKit.LowIterations},l=32$");
        PhcString.TryParse(encoded, out var phc).Should().BeTrue();
        phc!.Salt.Length.Should().Be(16);
        phc.Hash.Length.Should().Be(32);
    }

    [Fact]
    public void should_fail_when_secret_is_wrong()
    {
        // given
        var sut = SecretHashingTestKit.CreateHasher(SecretHashingTestKit.Pbkdf2Options());
        var encoded = sut.Hash("right");

        // when
        var result = sut.Verify("wrong", encoded);

        // then
        result.Should().Be(SecretVerification.Failed);
    }

    [Fact]
    public void should_rehash_when_stored_iterations_are_below_configured()
    {
        // given
        var stored = SecretHashingTestKit
            .CreateHasher(SecretHashingTestKit.Pbkdf2Options(iterations: 1_000))
            .Hash("pin");
        var sut = SecretHashingTestKit.CreateHasher(SecretHashingTestKit.Pbkdf2Options(iterations: 2_000));

        // when
        var result = sut.Verify("pin", stored);

        // then
        result.Succeeded.Should().BeTrue();
        result.Rehashed.Should().StartWith("$pbkdf2-sha256$i=2000,l=32$");
        sut.Verify("pin", result.Rehashed!).Should().Be(new SecretVerification(true, null));
    }

    [Fact]
    public void should_not_rehash_when_every_stored_parameter_is_at_or_above_configured()
    {
        // given
        var stored = SecretHashingTestKit
            .CreateHasher(SecretHashingTestKit.Pbkdf2Options(iterations: 2_000))
            .Hash("pin");
        var sut = SecretHashingTestKit.CreateHasher(SecretHashingTestKit.Pbkdf2Options(iterations: 1_000));

        // when
        var result = sut.Verify("pin", stored);

        // then
        result.Should().Be(new SecretVerification(true, null));
    }

    [Fact]
    public void should_rehash_when_any_stored_parameter_is_below_configured_even_if_another_is_higher()
    {
        // given
        var stored = SecretHashingTestKit
            .CreateHasher(SecretHashingTestKit.Pbkdf2Options(iterations: 2_000, hashSize: 16))
            .Hash("pin");
        var sut = SecretHashingTestKit.CreateHasher(
            SecretHashingTestKit.Pbkdf2Options(iterations: 1_000, hashSize: 32)
        );

        // when
        var result = sut.Verify("pin", stored);

        // then
        result.Succeeded.Should().BeTrue();
        result.Rehashed.Should().StartWith("$pbkdf2-sha256$i=1000,l=32$");
    }

    [Fact]
    public void should_rehash_under_the_configured_algorithm_when_stored_algorithm_differs()
    {
        // given
        var stored = SecretHashingTestKit.CreateHasher(SecretHashingTestKit.Pbkdf2Options()).Hash("pin");
        var options = SecretHashingTestKit.Pbkdf2Options();
        options.Algorithm = "stub";
        var stub = new RecordingSecretHashAlgorithm();
        var sut = SecretHashingTestKit.CreateHasher(options, stub);

        // when
        var result = sut.Verify("pin", stored);

        // then
        result.Succeeded.Should().BeTrue();
        result.Rehashed.Should().StartWith("$stub$");
        stub.HashCalls.Should().Be(1);
        sut.Verify("pin", result.Rehashed!).Should().Be(new SecretVerification(true, null));
    }

    [Fact]
    public void should_not_rehash_on_failure()
    {
        // given
        var stored = SecretHashingTestKit
            .CreateHasher(SecretHashingTestKit.Pbkdf2Options(iterations: 1_000))
            .Hash("pin");
        var sut = SecretHashingTestKit.CreateHasher(SecretHashingTestKit.Pbkdf2Options(iterations: 2_000));

        // when
        var result = sut.Verify("nope", stored);

        // then
        result.Should().Be(SecretVerification.Failed);
    }

    [Theory]
    [InlineData("$bcrypt$c29tZXNhbHRzb21lc2FsdA$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g")]
    [InlineData("garbage")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("$pbkdf2-sha256$i=1000$c29tZXNhbHRzb21lc2FsdA$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g")]
    public void should_fail_without_throwing_for_malformed_or_unknown_encodings(string encoded)
    {
        // given
        var sut = SecretHashingTestKit.CreateHasher(SecretHashingTestKit.Pbkdf2Options());

        // when
        var result = sut.Verify("pin", encoded);

        // then
        result.Should().Be(SecretVerification.Failed);
    }

    [Fact]
    public void should_throw_when_encoded_is_null()
    {
        var sut = SecretHashingTestKit.CreateHasher(SecretHashingTestKit.Pbkdf2Options());

        FluentActions.Invoking(() => sut.Verify("pin", null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void should_reject_secret_longer_than_max_length()
    {
        // given
        var options = SecretHashingTestKit.Pbkdf2Options();
        options.MaxSecretLength = 8;
        var sut = SecretHashingTestKit.CreateHasher(options);
        var stored = sut.Hash("12345678");

        // when
        var tooLong = new string('x', 9);

        // then
        FluentActions.Invoking(() => sut.Hash(tooLong)).Should().Throw<ArgumentException>();
        sut.Verify(tooLong, stored).Should().Be(SecretVerification.Failed);
    }

    [Fact]
    public void should_reject_empty_secret_when_hashing()
    {
        var sut = SecretHashingTestKit.CreateHasher(SecretHashingTestKit.Pbkdf2Options());

        FluentActions.Invoking(() => sut.Hash("")).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_reject_lone_surrogate()
    {
        // given
        var sut = SecretHashingTestKit.CreateHasher(SecretHashingTestKit.Pbkdf2Options());
        var stored = sut.Hash("pin");
        var invalid = "pin\uD800";

        // then
        FluentActions.Invoking(() => sut.Hash(invalid)).Should().Throw<ArgumentException>();
        sut.Verify(invalid, stored).Should().Be(SecretVerification.Failed);
    }

    [Theory]
    [InlineData("pässwörd")]
    [InlineData("密码🔑")]
    [InlineData("nul\0inside")]
    public void should_hash_and_verify_non_ascii_secrets(string secret)
    {
        // given
        var sut = SecretHashingTestKit.CreateHasher(SecretHashingTestKit.Pbkdf2Options());

        // when
        var encoded = sut.Hash(secret);

        // then
        sut.Verify(secret, encoded).Succeeded.Should().BeTrue();
        sut.Verify(secret.Normalize(NormalizationForm.FormD) + "x", encoded).Succeeded.Should().BeFalse();
    }

    [Fact]
    public void should_throw_naming_the_argon2_package_when_configured_algorithm_is_not_registered()
    {
        // given
        var options = SecretHashingTestKit.Pbkdf2Options();
        options.Algorithm = SecretHashAlgorithms.Argon2id;
        var sut = SecretHashingTestKit.CreateHasher(options);

        // then
        FluentActions
            .Invoking(() => sut.Hash("pin"))
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Headless.Security.Argon2*");
    }

    [Fact]
    public void should_run_exactly_one_derivation_at_the_stored_length_regardless_of_wrong_secret_length()
    {
        // given
        var options = SecretHashingTestKit.Pbkdf2Options();
        options.Algorithm = "stub";
        var stub = new RecordingSecretHashAlgorithm();
        var sut = SecretHashingTestKit.CreateHasher(options, stub);
        var stored = sut.Hash("123456");

        // when
        sut.Verify("654321", stored).Succeeded.Should().BeFalse();
        sut.Verify("1", stored).Succeeded.Should().BeFalse();
        sut.Verify(new string('9', 200), stored).Succeeded.Should().BeFalse();

        // then
        stub.DerivationLengths.Should().Equal(32, 32, 32);
    }
}
