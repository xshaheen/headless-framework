// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Tests.Primitives;

public sealed class ExtraPropertiesTests
{
    private enum Color
    {
        Red,
        Green,
        Blue,
    }

    [Fact]
    public void to_enum_should_parse_string_value()
    {
        // given
        var props = new ExtraProperties { ["color"] = "Green" };

        // when
        var result = props.ToEnum<Color>("color");

        // then
        result.Should().Be(Color.Green);
    }

    [Fact]
    public void to_enum_should_parse_string_value_case_insensitively()
    {
        // given
        var props = new ExtraProperties { ["color"] = "blue" };

        // when
        var result = props.ToEnum<Color>("color");

        // then
        result.Should().Be(Color.Blue);
    }

    [Fact]
    public void to_enum_should_return_existing_enum_value()
    {
        // given
        var props = new ExtraProperties { ["color"] = Color.Red };

        // when
        var result = props.ToEnum<Color>("color");

        // then
        result.Should().Be(Color.Red);
    }

    [Fact]
    public void to_enum_should_return_same_value_on_repeated_reads()
    {
        // given - first read parses and caches the string as the typed enum
        var props = new ExtraProperties { ["color"] = "Green" };

        // when
        var first = props.ToEnum<Color>("color");
        var second = props.ToEnum<Color>("color");

        // then
        first.Should().Be(Color.Green);
        second.Should().Be(Color.Green);
    }
}
