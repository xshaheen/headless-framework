// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Cryptography;
using Headless.Security;
using Microsoft.Extensions.Options;

namespace Tests.SecretHashing;

public sealed class Pbkdf2Sha256SecretHashAlgorithmTests
{
    private readonly Pbkdf2Sha256SecretHashAlgorithm _sut = new(Options.Create(SecretHashingTestKit.Pbkdf2Options()));

    [Fact]
    public void should_match_the_bcl_pbkdf2_derivation()
    {
        // given
        var encoded = _sut.Hash("password"u8);
        PhcString.TryParse(encoded, out var phc).Should().BeTrue();
        var expected = Rfc2898DeriveBytes.Pbkdf2(
            "password"u8,
            phc!.Salt,
            SecretHashingTestKit.LowIterations,
            HashAlgorithmName.SHA256,
            32
        );

        // when
        var destination = new byte[32];
        var computed = _sut.TryComputeHash("password"u8, phc, destination);

        // then
        computed.Should().BeTrue();
        destination.Should().Equal(expected);
        phc.Hash.ToArray().Should().Equal(expected);
    }

    [Theory]
    [InlineData("i=10000001,l=32")] // above the iteration cap
    [InlineData("i=0,l=32")] // below the primitive's minimum
    [InlineData("i=1000,l=16")] // declared length disagrees with the hash
    [InlineData("i=1000")] // missing length
    [InlineData("l=32,i=1000")] // reordered
    [InlineData("i=1000,l=32,x=1")] // unknown parameter
    [InlineData("i=01000,l=32")] // non-canonical decimal
    public void should_refuse_out_of_bounds_or_unexpected_parameters_without_deriving(string parameters)
    {
        // given
        var encoded = $"$pbkdf2-sha256${parameters}$c29tZXNhbHRzb21lc2FsdA$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g";
        PhcString.TryParse(encoded, out var phc).Should().BeTrue();

        // when
        var computed = _sut.TryComputeHash("password"u8, phc!, new byte[32]);

        // then
        computed.Should().BeFalse();
    }

    [Fact]
    public void should_refuse_a_version_segment()
    {
        PhcString
            .TryParse(
                "$pbkdf2-sha256$v=1$i=1000,l=32$c29tZXNhbHRzb21lc2FsdA$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g",
                out var phc
            )
            .Should()
            .BeTrue();

        _sut.TryComputeHash("password"u8, phc!, new byte[32]).Should().BeFalse();
    }

    [Fact]
    public void should_refuse_a_salt_shorter_than_the_minimum()
    {
        // 11-byte salt
        PhcString
            .TryParse(
                "$pbkdf2-sha256$i=1000,l=32$c2hvcnRzYWx0MTI$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g",
                out var phc
            )
            .Should()
            .BeTrue();

        _sut.TryComputeHash("password"u8, phc!, new byte[32]).Should().BeFalse();
    }
}
