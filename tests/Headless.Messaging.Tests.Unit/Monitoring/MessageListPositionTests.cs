// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Testing.Tests;

namespace Tests.Monitoring;

public sealed class MessageListPositionTests : TestBase
{
    [Theory]
    [InlineData("orders.placed")]
    [InlineData("a:b|c=d")]
    [InlineData("")]
    [InlineData("طلب")]
    public void should_round_trip_a_position_whose_name_holds_any_text(string name)
    {
        // given
        var position = new MessageListPosition(
            new DateTimeOffset(2026, 11, 10, 8, 30, 0, TimeSpan.FromHours(2)),
            name,
            Guid.NewGuid()
        );

        // when
        var text = position.ToString();
        var parsed = MessageListPosition.TryParse(text, out var read);

        // then: the text never holds a separator a merged cursor uses
        parsed.Should().BeTrue();
        read.Should().Be(position);
        text.Count(static c => c == ':').Should().Be(3);
        text.Should().NotContain("|").And.NotContain("=");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("l1:638000000000000000:0192f3c17a1b7c3d9e4f0a1b2c3d4e5f")]
    [InlineData("l2:638000000000000000:!!:0192f3c17a1b7c3d9e4f0a1b2c3d4e5f")]
    [InlineData("l2:-1:YQ:0192f3c17a1b7c3d9e4f0a1b2c3d4e5f")]
    [InlineData("l2:638000000000000000:YQ:not-a-guid")]
    public void should_refuse_text_that_is_not_a_position(string? text)
    {
        MessageListPosition.TryParse(text, out _).Should().BeFalse();
    }
}
