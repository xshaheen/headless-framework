// Copyright (c) Mahmoud Shaheen. All rights reserved.

using AwesomeAssertions.Collections;
using Headless.Security;

namespace Tests.Assertions;

public sealed class SecretHashAssertionsTests
{
    private const string _Argon2 =
        "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHRzb21lc2FsdA$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g";

    private const string _Pbkdf2 =
        "$pbkdf2-sha256$i=600000,l=32$c29tZXNhbHRzb21lc2FsdA$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g";

    [Fact]
    public void should_pass_when_every_value_is_a_hash_of_the_expected_algorithm()
    {
        string[] column = [_Argon2, _Argon2];

        column.Should().AllBeSecretHashes(SecretHashAlgorithms.Argon2id);
    }

    [Fact]
    public void should_pass_for_an_empty_column()
    {
        Array.Empty<string>().Should().AllBeSecretHashes(SecretHashAlgorithms.Argon2id);
    }

    [Fact]
    public void should_fail_naming_the_index_when_a_value_uses_another_algorithm()
    {
        string[] column = [_Argon2, _Pbkdf2];

        FluentActions
            .Invoking(() => column.Should().AllBeSecretHashes(SecretHashAlgorithms.Argon2id))
            .Should()
            .Throw<Exception>()
            .WithMessage("*index 1*algorithm*pbkdf2-sha256*");
    }

    [Fact]
    public void should_fail_without_echoing_a_plaintext_value()
    {
        string[] column = [_Argon2, "hunter2-plaintext"];

        FluentActions
            .Invoking(() => column.Should().AllBeSecretHashes(SecretHashAlgorithms.Argon2id))
            .Should()
            .Throw<Exception>()
            .Where(e => e.Message.Contains("index 1") && e.Message.Contains("not a PHC string"))
            .Where(e => !e.Message.Contains("hunter2"));
    }

    [Fact]
    public void should_fail_for_a_null_collection()
    {
        string[]? column = null;

        FluentActions
            .Invoking(() => column.Should().AllBeSecretHashes(SecretHashAlgorithms.Argon2id))
            .Should()
            .Throw<Exception>()
            .WithMessage("*<null>*");
    }

    [Fact]
    public void should_fail_for_a_null_value()
    {
        string?[] column = [_Argon2, null];

        FluentActions
            .Invoking(() => column.Should().AllBeSecretHashes(SecretHashAlgorithms.Argon2id))
            .Should()
            .Throw<Exception>()
            .WithMessage("*index 1*");
    }

    [Fact]
    public void should_include_the_because_reason()
    {
        string[] column = ["plain"];

        FluentActions
            .Invoking(() => column.Should().AllBeSecretHashes(SecretHashAlgorithms.Argon2id, "PINs are {0}", "hashed"))
            .Should()
            .Throw<Exception>()
            .WithMessage("*because PINs are hashed*");
    }
}
