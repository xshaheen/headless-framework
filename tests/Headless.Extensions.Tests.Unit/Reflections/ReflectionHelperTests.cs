// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Reflection;

namespace Tests.Reflections;

public sealed class ReflectionHelperTests
{
    [Fact]
    public void is_flags_enum_returns_true_for_flags_enum()
    {
        typeof(SampleFlags).IsFlagsEnum().Should().BeTrue();
        ReflectionHelper.IsFlagsEnum<SampleFlags>().Should().BeTrue();
    }

    [Fact]
    public void is_flags_enum_returns_false_for_non_flags_enum()
    {
        typeof(SamplePlain).IsFlagsEnum().Should().BeFalse();
        ReflectionHelper.IsFlagsEnum<SamplePlain>().Should().BeFalse();
    }

    [Fact]
    public void is_flags_enum_throws_for_null_type()
    {
        var act = () => ((Type)null!).IsFlagsEnum();

        act.Should().Throw<ArgumentNullException>();
    }

    [Flags]
    private enum SampleFlags
    {
        None = 0,
        First = 1,
        Second = 2,
    }

    private enum SamplePlain
    {
        First,
        Second,
    }
}
