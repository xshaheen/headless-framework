// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Security;

namespace Tests.Abstractions;

public sealed class PhcStringTests
{
    // 16-byte salt and 32-byte hash, unpadded standard base64.
    private const string _Salt = "c29tZXNhbHRzb21lc2FsdA";
    private const string _Hash = "aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g";

    [Fact]
    public void should_round_trip_argon2id_shape_byte_identical()
    {
        // given
        const string encoded = $"$argon2id$v=19$m=19456,t=2,p=1${_Salt}${_Hash}";

        // when
        var parsed = PhcString.TryParse(encoded, out var phc);

        // then
        parsed.Should().BeTrue();
        phc!.Id.Should().Be("argon2id");
        phc.Version.Should().Be(19);
        phc.Parameters.Should()
            .Equal(new PhcParameter("m", "19456"), new PhcParameter("t", "2"), new PhcParameter("p", "1"));
        phc.Salt.Length.Should().Be(16);
        phc.Hash.Length.Should().Be(32);
        phc.ToString().Should().Be(encoded);
    }

    [Fact]
    public void should_round_trip_pbkdf2_shape_without_version()
    {
        // given
        const string encoded = $"$pbkdf2-sha256$i=600000,l=32${_Salt}${_Hash}";

        // when
        var parsed = PhcString.TryParse(encoded, out var phc);

        // then
        parsed.Should().BeTrue();
        phc!.Version.Should().BeNull();
        phc.TryGetInt32("i", out var iterations).Should().BeTrue();
        iterations.Should().Be(600_000);
        phc.ToString().Should().Be(encoded);
    }

    [Fact]
    public void should_parse_when_only_salt_and_hash_follow_the_id()
    {
        PhcString.TryParse($"$x${_Salt}${_Hash}", out var phc).Should().BeTrue();
        phc!.Parameters.Should().BeEmpty();
        phc.Version.Should().BeNull();
    }

    [Theory]
    [InlineData($"$argon2id$v=19$m=1,t=2,p=1$c29tZXNhbHRzb21lc2FsdA==${_Hash}")] // padded salt
    [InlineData($"$argon2id$v=19$m=1,t=2,p=1${_Salt}$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2h")] // non-zero unused bits
    [InlineData($"$argon2id$v=19$m=1,t=2,p=1$c29tZXNhbHRz-21lc2FsdA${_Hash}")] // url-safe alphabet
    [InlineData($"$argon2id$v=19$m=1,m=2${_Salt}${_Hash}")] // duplicate parameter
    [InlineData($"$argon2id$v=019$m=1${_Salt}${_Hash}")] // leading-zero version
    [InlineData($"argon2id$v=19$m=1${_Salt}${_Hash}")] // missing leading '$'
    [InlineData($"$$v=19$m=1${_Salt}${_Hash}")] // empty id
    [InlineData($"$Argon2id$v=19$m=1${_Salt}${_Hash}")] // uppercase id
    [InlineData($"$argon2id$v=19$m=1$k=2${_Salt}${_Hash}")] // too many segments
    [InlineData($"$argon2id$v=19$m=1${_Salt}$")] // empty hash
    [InlineData($"$argon2id$v=19$m=${_Salt}${_Hash}")] // empty parameter value
    [InlineData($"$argon2id$v=19$=1${_Salt}${_Hash}")] // empty parameter name
    [InlineData("$argon2id")] // no salt or hash
    [InlineData("")]
    [InlineData(" ")]
    public void should_reject_non_canonical_or_malformed_input(string encoded)
    {
        PhcString.TryParse(encoded, out var phc).Should().BeFalse();
        phc.Should().BeNull();
    }

    [Fact]
    public void should_reject_null()
    {
        PhcString.TryParse(null, out _).Should().BeFalse();
    }

    [Fact]
    public void should_reject_input_longer_than_max_length()
    {
        var encoded = "$x$" + new string('A', PhcString.MaxLength) + "$" + _Hash;

        PhcString.TryParse(encoded, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("02", false)]
    [InlineData("0", true)]
    [InlineData("-1", false)]
    [InlineData("99999999999", false)]
    [InlineData("1a", false)]
    public void should_read_only_canonical_decimal_parameters(string value, bool expected)
    {
        // given
        PhcString.TryParse($"$x$t={value}${_Salt}${_Hash}", out var phc).Should().BeTrue();

        // when
        var read = phc!.TryGetInt32("t", out _);

        // then
        read.Should().Be(expected);
    }

    [Fact]
    public void should_report_missing_parameter()
    {
        PhcString.TryParse($"$x$t=1${_Salt}${_Hash}", out var phc).Should().BeTrue();

        phc!.TryGetInt32("m", out _).Should().BeFalse();
    }

    [Fact]
    public void should_reject_constructing_with_invalid_parts()
    {
        var salt = new byte[16];
        var hash = new byte[32];

        FluentActions.Invoking(() => new PhcString("BAD", null, [], salt, hash)).Should().Throw<ArgumentException>();
        FluentActions
            .Invoking(() => new PhcString("x", null, [new("a", "1"), new("a", "2")], salt, hash))
            .Should()
            .Throw<ArgumentException>();
        FluentActions.Invoking(() => new PhcString("x", null, [], [], hash)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new PhcString("x", -1, [], salt, hash)).Should().Throw<ArgumentException>();
    }
}
