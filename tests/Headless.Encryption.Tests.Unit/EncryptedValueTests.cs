// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Encryption;
using Headless.Testing.Tests;

namespace Tests;

public sealed class EncryptedValueTests : TestBase
{
    [Fact]
    public void should_read_the_key_id_and_round_trip_through_base64url()
    {
        var keyId = Guid.CreateVersion7();
        var frame = _Frame(keyId, ciphertextLength: 5);

        var value = EncryptedValue.FromBytes(frame);
        var parsed = EncryptedValue.Parse(value.ToBase64Url());

        value.KeyId.Should().Be(keyId);
        value.Length.Should().Be(EncryptedValue.Overhead + 5);
        parsed.Should().Be(value);
        parsed.GetHashCode().Should().Be(value.GetHashCode());
        value.ToBase64Url().Should().NotContainAny("+", "/", "=");
    }

    [Fact]
    public void should_copy_the_bytes_it_is_given()
    {
        var frame = _Frame(Guid.CreateVersion7(), ciphertextLength: 1);
        var value = EncryptedValue.FromBytes(frame);

        frame[^1] ^= 0xFF;

        value.Frame.Span[^1].Should().NotBe(frame[^1]);
        value.ToArray().Should().NotBeSameAs(value.ToArray());
    }

    [Fact]
    public void should_refuse_bytes_shorter_than_a_frame()
    {
        var act = () => EncryptedValue.FromBytes(new byte[EncryptedValue.Overhead - 1]);

        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void should_refuse_an_unknown_format_version()
    {
        var frame = _Frame(Guid.CreateVersion7(), ciphertextLength: 0);
        frame[0] = 0x02;

        EncryptedValue.TryFromBytes(frame, out var value).Should().BeFalse();
        value.IsDefault.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not base64url!")]
    [InlineData("AQ")]
    public void should_not_parse_text_that_is_not_a_frame(string? text)
    {
        EncryptedValue.TryParse(text, out var value).Should().BeFalse();
        value.IsDefault.Should().BeTrue();
    }

    [Fact]
    public void should_describe_the_default_value_as_empty()
    {
        var value = default(EncryptedValue);

        value.IsDefault.Should().BeTrue();
        value.Length.Should().Be(0);
        value.ToArray().Should().BeEmpty();
        value.ToBase64Url().Should().BeEmpty();
        value.Should().Be(default(EncryptedValue));
        var keyId = () => value.KeyId;
        keyId.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void should_compare_by_frame_bytes_through_every_equality_member()
    {
        var frame = _Frame(Guid.CreateVersion7(), ciphertextLength: 3);
        var value = EncryptedValue.FromBytes(frame);
        var same = EncryptedValue.FromBytes(frame);
        frame[^1] ^= 0x01;
        var other = EncryptedValue.FromBytes(frame);

        (value == same).Should().BeTrue();
        (value != other).Should().BeTrue();
        value.Equals((object)same).Should().BeTrue();
        value.Equals((object)"not a value").Should().BeFalse();
        value.ToString().Should().Be(value.ToBase64Url());
        default(EncryptedValue).GetHashCode().Should().Be(0);
    }

    [Fact]
    public void should_refuse_to_parse_text_that_is_not_a_frame()
    {
        var notBase64 = () => EncryptedValue.Parse("not base64url!");
        var tooShort = () => EncryptedValue.Parse("AQ");
        var wrongVersion = _Frame(Guid.CreateVersion7(), ciphertextLength: 0);
        wrongVersion[0] = 0x02;
        var unknownVersion = () =>
            EncryptedValue.Parse(Convert.ToBase64String(wrongVersion).TrimEnd('=').Replace('+', '-').Replace('/', '_'));

        notBase64.Should().Throw<FormatException>();
        tooShort.Should().Throw<FormatException>();
        unknownVersion.Should().Throw<FormatException>();
    }

    private static byte[] _Frame(Guid keyId, int ciphertextLength)
    {
        var frame = new byte[EncryptedValue.Overhead + ciphertextLength];
        frame[0] = EncryptedValue.FormatVersion;
        frame[1] = 0x01;
        keyId.TryWriteBytes(frame.AsSpan(2, 16), bigEndian: true, out _);
        Random.Shared.NextBytes(frame.AsSpan(18));

        return frame;
    }
}
