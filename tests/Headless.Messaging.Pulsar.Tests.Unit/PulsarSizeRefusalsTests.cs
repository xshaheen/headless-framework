// Copyright (c) Mahmoud Shaheen. All rights reserved.

using DotPulsar.Exceptions;
using Headless.Messaging.Pulsar;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class PulsarSizeRefusalsTests : TestBase
{
    private readonly FakeTimeProvider _time = new();

    [Fact]
    public void should_match_a_refusal_whose_frame_is_the_payload_plus_its_overhead()
    {
        // given
        var refusals = new PulsarSizeRefusals(_time);
        var refusal = new TooLargeMessageException(5_000_300, 5_242_880);
        refusals.Record(refusal);

        // when, then
        refusals.TryTake(5_000_000, out var taken).Should().BeTrue();
        taken.Should().BeSameAs(refusal);
        refusals.TryTake(5_000_000, out _).Should().BeFalse("a refusal answers one send");
    }

    [Theory]
    [InlineData(5_000_301)]
    [InlineData(4_000_000)]
    public void should_not_match_a_refusal_of_another_message_size(long payloadLength)
    {
        // given
        var refusals = new PulsarSizeRefusals(_time);
        refusals.Record(new TooLargeMessageException(5_000_300, 5_242_880));

        // when, then
        refusals.TryTake(payloadLength, out _).Should().BeFalse();
    }

    [Fact]
    public void should_forget_a_refusal_older_than_a_minute()
    {
        // given
        var refusals = new PulsarSizeRefusals(_time);
        refusals.Record(new TooLargeMessageException(5_000_300, 5_242_880));

        // when
        _time.Advance(TimeSpan.FromMinutes(2));

        // then
        refusals.TryTake(5_000_000, out _).Should().BeFalse();
    }
}
