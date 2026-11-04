// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Encryption;
using Headless.Testing.Tests;

namespace Tests;

public sealed class BlindIndexTextTests : TestBase
{
    [Theory]
    [InlineData(1, BlindIndexContext.MinLength)]
    [InlineData(7, BlindIndexContext.MaxLength)]
    [InlineData(int.MaxValue, 16)]
    public void should_round_trip_the_key_version_and_hash_through_base64url(int keyVersion, int length)
    {
        var index = new BlindIndex(keyVersion, _Hash(length));

        var text = index.ToBase64Url();
        var parsed = BlindIndex.Parse(text);

        parsed.Should().Be(index);
        parsed.KeyVersion.Should().Be(keyVersion);
        parsed.Hash.Should().Equal(index.Hash);
        text.Should().NotContainAny("+", "/", "=");
        index.ToString().Should().Be(text);
    }

    [Fact]
    public void should_write_the_same_text_for_the_same_version_and_hash()
    {
        var hash = _Hash(BlindIndexContext.MaxLength);

        new BlindIndex(3, hash).ToBase64Url().Should().Be(new BlindIndex(3, [.. hash]).ToBase64Url());
        new BlindIndex(3, hash).ToBase64Url().Should().NotBe(new BlindIndex(4, hash).ToBase64Url());
    }

    [Fact]
    public void should_write_empty_text_for_the_default_value()
    {
        default(BlindIndex).ToBase64Url().Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not base64url!")]
    [InlineData("AAAAAQ")] // a key version with no hash
    public void should_refuse_text_that_is_not_a_blind_index(string? text)
    {
        BlindIndex.TryParse(text, out var index).Should().BeFalse();
        index.Should().Be(default(BlindIndex));
    }

    [Theory]
    [InlineData(BlindIndexContext.MinLength - 1)]
    [InlineData(BlindIndexContext.MaxLength + 1)]
    public void should_refuse_a_hash_outside_the_supported_lengths(int length)
    {
        var text = new BlindIndex(1, _Hash(length)).ToBase64Url();

        BlindIndex.TryParse(text, out _).Should().BeFalse();
        var act = () => BlindIndex.Parse(text);
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void should_reject_null_text_on_parse()
    {
        var act = () => BlindIndex.Parse(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    private static byte[] _Hash(int length)
    {
        var hash = new byte[length];
        Random.Shared.NextBytes(hash);

        return hash;
    }
}
