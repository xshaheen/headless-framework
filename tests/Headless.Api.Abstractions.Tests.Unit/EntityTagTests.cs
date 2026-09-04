// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Testing.Tests;

namespace Tests;

public sealed class EntityTagTests : TestBase
{
    [Fact]
    public void should_round_trip_binary_tokens()
    {
        byte[] token = [0, 1, 2, 3, 4, 250, 255];

        var entityTag = EntityTag.FromBytes(token);

        entityTag.HeaderValue.Should().Be("\"AAECAwT6/w==\"");
        entityTag.IsWeak.Should().BeFalse();
        entityTag.TryGetBytes(out var actual).Should().BeTrue();
        actual.Should().Equal(token);
    }

    [Fact]
    public void should_encode_uint_versions_in_network_byte_order()
    {
        var entityTag = EntityTag.FromUInt32(0x01020304);

        entityTag.HeaderValue.Should().Be("\"AQIDBA==\"");
        entityTag.TryGetUInt32(out var actual).Should().BeTrue();
        actual.Should().Be(0x01020304);
    }

    [Theory]
    [InlineData("\"revision-42\"", false, "revision-42")]
    [InlineData("W/\"revision-42\"", true, "revision-42")]
    public void should_parse_valid_entity_tags(string value, bool isWeak, string opaqueValue)
    {
        var parsed = EntityTag.TryParse(value, out var entityTag);

        parsed.Should().BeTrue();
        entityTag.Should().NotBeNull();
        entityTag!.HeaderValue.Should().Be(value);
        entityTag.IsWeak.Should().Be(isWeak);
        entityTag.OpaqueValue.Should().Be(opaqueValue);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("*")]
    [InlineData("revision-42")]
    [InlineData("\"one\", \"two\"")]
    public void should_reject_values_that_are_not_single_entity_tags(string? value)
    {
        EntityTag.TryParse(value, out _).Should().BeFalse();
    }
}
