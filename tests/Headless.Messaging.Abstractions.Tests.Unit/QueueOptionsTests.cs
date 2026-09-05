// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Testing.Tests;

namespace Tests;

public sealed class QueueOptionsTests : TestBase
{
    [Fact]
    public void should_default_delivery_mode_to_durable()
    {
        // when
        var options = new QueueOptions();

        // then
        options.DeliveryMode.Should().Be(DeliveryMode.Durable);
    }

    [Fact]
    public void should_include_delivery_mode_and_delay_in_equality_and_hashing()
    {
        // given
        var expected = new QueueOptions { DeliveryMode = DeliveryMode.Durable, Delay = TimeSpan.FromMinutes(1) };
        var equivalent = new QueueOptions { DeliveryMode = DeliveryMode.Durable, Delay = TimeSpan.FromMinutes(1) };
        var differentMode = expected with { DeliveryMode = DeliveryMode.Auto };
        var differentDelay = expected with { Delay = TimeSpan.FromMinutes(2) };

        // then
        expected.Should().Be(equivalent);
        expected.GetHashCode().Should().Be(equivalent.GetHashCode());
        expected.Should().NotBe(differentMode);
        expected.GetHashCode().Should().NotBe(differentMode.GetHashCode());
        expected.Should().NotBe(differentDelay);
        expected.GetHashCode().Should().NotBe(differentDelay.GetHashCode());
    }

    [Fact]
    public void should_not_expose_lane_override()
    {
        // then
        typeof(QueueOptions).GetProperties().Should().NotContain(property => property.Name == "Lane");
    }
}
