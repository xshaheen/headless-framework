// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Testing.Tests;

namespace Tests;

public sealed class RequestOptionsTests : TestBase
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void should_reject_a_timeout_that_is_not_positive(int milliseconds)
    {
        // when
        var act = () => new RequestOptions { Timeout = TimeSpan.FromMilliseconds(milliseconds) };

        // then
        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName(nameof(RequestOptions.Timeout));
    }

    [Fact]
    public void should_accept_a_positive_timeout()
    {
        // when
        var options = new RequestOptions { Timeout = TimeSpan.FromSeconds(5) };

        // then
        options.Timeout.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void should_accept_a_timeout_at_the_upper_bound()
    {
        // when
        var options = new RequestOptions { Timeout = RequestOptions.MaxTimeout };

        // then
        options.Timeout.Should().Be(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public void should_reject_a_timeout_longer_than_the_upper_bound()
    {
        // given
        var timeout = RequestOptions.MaxTimeout + TimeSpan.FromTicks(1);

        // when
        var act = () => new RequestOptions { Timeout = timeout };

        // then
        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName(nameof(RequestOptions.Timeout));
    }

    [Fact]
    public void should_leave_the_timeout_unset_by_default_so_the_host_default_applies()
    {
        // when
        var options = new RequestOptions();

        // then
        options.Timeout.Should().BeNull();
    }

    [Fact]
    public void should_not_expose_options_a_request_must_refuse()
    {
        // when
        var properties = typeof(RequestOptions).GetProperties().Select(property => property.Name).ToArray();

        // then
        properties
            .Should()
            .NotContain([
                nameof(MessageOptions.MessageId),
                nameof(MessageOptions.Delay),
                nameof(MessageOptions.ScheduledAt),
                nameof(MessageOptions.CallbackName),
                nameof(QueueOptions.DeliveryMode),
            ]);
        typeof(RequestOptions).IsAssignableTo(typeof(MessageOptions)).Should().BeFalse();
    }
}
