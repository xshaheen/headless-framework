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
    public void should_parse_string_value_when_to_enum()
    {
        // given
        var props = new ExtraProperties { ["color"] = "Green" };

        // when
        var result = props.ToEnum<Color>("color");

        // then
        result.Should().Be(Color.Green);
    }

    [Fact]
    public void should_parse_string_value_case_insensitively_when_to_enum()
    {
        // given
        var props = new ExtraProperties { ["color"] = "blue" };

        // when
        var result = props.ToEnum<Color>("color");

        // then
        result.Should().Be(Color.Blue);
    }

    [Fact]
    public void should_return_existing_enum_value_when_to_enum()
    {
        // given
        var props = new ExtraProperties { ["color"] = Color.Red };

        // when
        var result = props.ToEnum<Color>("color");

        // then
        result.Should().Be(Color.Red);
    }

    [Fact]
    public void should_return_same_value_on_repeated_reads_without_mutating_the_bag_when_to_enum()
    {
        // given
        var props = new ExtraProperties { ["color"] = "Green" };

        // when - each read parses fresh; ToEnum is a pure read accessor and must not write the parsed enum back
        var first = props.ToEnum<Color>("color");
        var second = props.ToEnum<Color>("color");

        // then
        first.Should().Be(Color.Green);
        second.Should().Be(Color.Green);
        props["color"].Should().Be("Green"); // unchanged (no write-back into the dictionary)
        props["color"].Should().BeOfType<string>();
    }

    [Fact]
    public void should_be_true_for_equal_values_when_has_same_items()
    {
        // given
        var a = new ExtraProperties { ["n"] = 1, ["s"] = "x" };
        var b = new ExtraProperties { ["n"] = 1, ["s"] = "x" };

        // then
        a.HasSameItems(b).Should().BeTrue();
    }

    [Fact]
    public void should_distinguish_int_from_its_string_representation_when_has_same_items()
    {
        // given - 1 (int) and "1" (string) share a ToString() but are not equal values
        var a = new ExtraProperties { ["n"] = 1 };
        var b = new ExtraProperties { ["n"] = "1" };

        // then
        a.HasSameItems(b).Should().BeFalse();
    }
}
