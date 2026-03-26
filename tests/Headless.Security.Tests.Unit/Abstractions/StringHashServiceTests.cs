// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Cryptography;
using Headless;
using Headless.Abstractions;

namespace Tests.Abstractions;

public sealed class StringHashServiceTests
{
    private static StringHashOptions _CreateValidOptions() =>
        new()
        {
            Iterations = 10_000,
            Size = 32,
            Algorithm = HashAlgorithmName.SHA256,
            DefaultSalt = "DefaultSalt",
        };

    [Fact]
    public void should_use_default_salt_when_no_salt_is_provided()
    {
        // given
        var sut = new StringHashService(_CreateValidOptions());

        // when
        var hash1 = sut.Create("Hello");
        var hash2 = sut.Create("Hello");

        // then
        hash1.Should().Be(hash2);
    }

    [Fact]
    public void should_use_custom_salt_when_provided()
    {
        // given
        var sut = new StringHashService(_CreateValidOptions());

        // when
        var customHash = sut.Create("Hello", "CustomSalt");
        var defaultHash = sut.Create("Hello");

        // then
        customHash.Should().NotBe(defaultHash);
    }

    [Fact]
    public void should_use_empty_salt_when_default_salt_is_missing()
    {
        // given
        var options = new StringHashOptions
        {
            Iterations = 10_000,
            Size = 32,
            Algorithm = HashAlgorithmName.SHA256,
        };

        var sut = new StringHashService(options);

        // when
        var hash1 = sut.Create("Hello");
        var hash2 = sut.Create("Hello", string.Empty);

        // then
        hash1.Should().Be(hash2);
    }
}
