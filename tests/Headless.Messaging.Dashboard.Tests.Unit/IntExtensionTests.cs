// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Dashboard;
using Headless.Testing.Tests;
using Microsoft.Extensions.Primitives;

namespace Tests;

public sealed class IntExtensionTests : TestBase
{
    [Fact]
    public void ToInt32OrDefault_should_parse_valid_integer()
    {
        // given
        var value = new StringValues("42");

        // when
        var result = value.ToInt32OrDefault(0);

        // then
        result.Should().Be(42);
    }

    [Fact]
    public void ToInt32OrDefault_should_return_default_for_invalid_string()
    {
        // given
        var value = new StringValues("not-a-number");

        // when
        var result = value.ToInt32OrDefault(99);

        // then
        result.Should().Be(99);
    }

    [Fact]
    public void ToInt32OrDefault_should_return_default_for_empty_string()
    {
        // given
        var value = new StringValues("");

        // when
        var result = value.ToInt32OrDefault(10);

        // then
        result.Should().Be(10);
    }

    [Fact]
    public void ToInt32OrDefault_should_return_default_for_null()
    {
        // given
        var value = new StringValues((string?)null);

        // when
        var result = value.ToInt32OrDefault(5);

        // then
        result.Should().Be(5);
    }

    [Fact]
    public void ToInt32OrDefault_should_use_zero_as_default_when_not_specified()
    {
        // given
        var value = new StringValues("invalid");

        // when
        var result = value.ToInt32OrDefault();

        // then
        result.Should().Be(0);
    }

    [Fact]
    public void ToInt32OrDefault_should_parse_negative_integer()
    {
        // given
        var value = new StringValues("-123");

        // when
        var result = value.ToInt32OrDefault(0);

        // then
        result.Should().Be(-123);
    }

    [Fact]
    public void ToInt32OrDefault_should_parse_zero()
    {
        // given
        var value = new StringValues("0");

        // when
        var result = value.ToInt32OrDefault(99);

        // then
        result.Should().Be(0);
    }

    [Fact]
    public void ToInt32OrDefault_should_return_default_for_decimal_value()
    {
        // given
        var value = new StringValues("3.14");

        // when
        var result = value.ToInt32OrDefault(1);

        // then
        result.Should().Be(1);
    }

    [Fact]
    public void ToInt32OrDefault_should_return_default_for_overflow_value()
    {
        // given
        var value = new StringValues("99999999999999999999");

        // when
        var result = value.ToInt32OrDefault(7);

        // then
        result.Should().Be(7);
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("100", 100)]
    [InlineData("999", 999)]
    [InlineData("-1", -1)]
    public void ToInt32OrDefault_should_parse_various_valid_integers(string input, int expected)
    {
        // given
        var value = new StringValues(input);

        // when
        var result = value.ToInt32OrDefault(0);

        // then
        result.Should().Be(expected);
    }
}
