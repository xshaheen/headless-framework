// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests.Core;

public sealed class TimeSpanExtensionsTests
{
    [Fact]
    public void clamp_should_return_value_when_within_range()
    {
        // given
        var value = TimeSpan.FromMinutes(5);
        var min = TimeSpan.FromMinutes(1);
        var max = TimeSpan.FromMinutes(10);

        // when
        var result = value.Clamp(min, max);

        // then
        result.Should().Be(value);
    }

    [Fact]
    public void clamp_should_return_min_when_value_is_below()
    {
        // given
        var value = TimeSpan.FromSeconds(30);
        var min = TimeSpan.FromMinutes(1);
        var max = TimeSpan.FromMinutes(10);

        // when
        var result = value.Clamp(min, max);

        // then
        result.Should().Be(min);
    }

    [Fact]
    public void clamp_should_return_max_when_value_is_above()
    {
        // given
        var value = TimeSpan.FromMinutes(15);
        var min = TimeSpan.FromMinutes(1);
        var max = TimeSpan.FromMinutes(10);

        // when
        var result = value.Clamp(min, max);

        // then
        result.Should().Be(max);
    }

    [Fact]
    public void min_should_return_smaller_timespan()
    {
        // given
        var source = TimeSpan.FromMinutes(10);
        var other = TimeSpan.FromMinutes(5);

        // when
        var result = source.Min(other);

        // then
        result.Should().Be(other);
    }

    [Fact]
    public void min_should_return_source_when_smaller()
    {
        // given
        var source = TimeSpan.FromMinutes(3);
        var other = TimeSpan.FromMinutes(5);

        // when
        var result = source.Min(other);

        // then
        result.Should().Be(source);
    }

    [Fact]
    public void max_should_return_larger_timespan()
    {
        // given
        var source = TimeSpan.FromMinutes(3);
        var other = TimeSpan.FromMinutes(5);

        // when
        var result = source.Max(other);

        // then
        result.Should().Be(other);
    }

    [Fact]
    public void max_should_return_source_when_larger()
    {
        // given
        var source = TimeSpan.FromMinutes(10);
        var other = TimeSpan.FromMinutes(5);

        // when
        var result = source.Max(other);

        // then
        result.Should().Be(source);
    }

    [Fact]
    public void to_cancellation_token_source_should_return_cancelled_when_zero()
    {
        // given
        var timeout = TimeSpan.Zero;

        // when
        using var cts = timeout.ToCancellationTokenSource();

        // then
        cts.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public void to_cancellation_token_source_should_return_non_cancelled_when_positive()
    {
        // given
        var timeout = TimeSpan.FromMinutes(5);

        // when
        using var cts = timeout.ToCancellationTokenSource();

        // then
        cts.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public void to_cancellation_token_source_should_return_non_cancelled_when_negative()
    {
        // given
        var timeout = TimeSpan.FromMinutes(-5);

        // when
        using var cts = timeout.ToCancellationTokenSource();

        // then
        cts.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public void to_cancellation_token_source_with_token_should_return_cancelled_when_zero()
    {
        // given
        var timeout = TimeSpan.Zero;
        var token = CancellationToken.None;

        // when
        using var cts = timeout.ToCancellationTokenSource(token);

        // then
        cts.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public void to_cancellation_token_source_with_token_should_link_to_provided_token()
    {
        // given
        var timeout = TimeSpan.FromMinutes(5);
        using var linkedCts = new CancellationTokenSource();
        linkedCts.Cancel();

        // when
        using var cts = timeout.ToCancellationTokenSource(linkedCts.Token);

        // then
        cts.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public void nullable_to_cancellation_token_source_should_return_non_cancelled_when_null()
    {
        // given
        TimeSpan? timeout = null;

        // when
        using var cts = timeout.ToCancellationTokenSource();

        // then
        cts.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public void nullable_to_cancellation_token_source_should_use_value_when_provided()
    {
        // given
        TimeSpan? timeout = TimeSpan.Zero;

        // when
        using var cts = timeout.ToCancellationTokenSource();

        // then
        cts.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public void nullable_to_cancellation_token_source_with_default_should_use_default_when_null()
    {
        // given
        TimeSpan? timeout = null;
        var defaultTimeout = TimeSpan.Zero;

        // when
        using var cts = timeout.ToCancellationTokenSource(defaultTimeout);

        // then
        cts.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public void nullable_to_cancellation_token_source_with_default_should_use_value_when_provided()
    {
        // given
        TimeSpan? timeout = TimeSpan.FromMinutes(5);
        var defaultTimeout = TimeSpan.Zero;

        // when
        using var cts = timeout.ToCancellationTokenSource(defaultTimeout);

        // then
        cts.IsCancellationRequested.Should().BeFalse();
    }
}
