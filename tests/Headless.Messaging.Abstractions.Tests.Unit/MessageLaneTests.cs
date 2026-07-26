// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Testing.Tests;

namespace Tests;

public sealed class MessageLaneTests : TestBase
{
    [Fact]
    public void should_preserve_legacy_numeric_values()
    {
        ((short)MessageLane.Bus).Should().Be(0).And.Be((short)MessageLane.Bus);
        ((short)MessageLane.Queue).Should().Be(1).And.Be((short)MessageLane.Queue);
    }

    [Fact]
    public void should_remain_smallint_compatible_without_an_unknown_member()
    {
        Enum.GetUnderlyingType(typeof(MessageLane)).Should().Be<short>();
        Enum.GetNames<MessageLane>().Should().Equal(nameof(MessageLane.Bus), nameof(MessageLane.Queue));
    }

    [Theory]
    [InlineData(MessageLane.Bus, "Bus")]
    [InlineData(MessageLane.Queue, "Queue")]
    public void should_preserve_legacy_wire_names(MessageLane lane, string wireValue)
    {
        Headers.Intent.Should().Be("headless-intent");
        lane.ToString().Should().Be(wireValue);
    }
}
