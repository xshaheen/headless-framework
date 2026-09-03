// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Concurrency;
using Headless.Testing.Tests;

namespace Tests.Concurrency;

public sealed class EntityTagCodecTests : TestBase
{
    [Fact]
    public void should_round_trip_arbitrary_binary_tokens()
    {
        byte[] token = [0, 1, 2, 3, 4, 250, 255];

        var formatted = EntityTagCodec.Format(token);
        var parsed = EntityTagCodec.TryParseStrong(formatted, out var actual);

        parsed.Should().BeTrue();
        actual.Should().Equal(token);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("W/\"AQID\"")]
    [InlineData("\"AQID\", \"BAUG\"")]
    [InlineData("\"not base64\"")]
    public void should_reject_non_strong_single_base64_tags(string value)
    {
        EntityTagCodec.TryParseStrong(value, out _).Should().BeFalse();
    }
}
