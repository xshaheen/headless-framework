// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Collections;

namespace Tests.Collections;

public sealed class ComparerFactoryTests
{
    [Fact]
    public void should_treat_equal_keys_as_equal_when_key_based_comparer()
    {
        // given
        var comparer = ComparerFactory.Create<Person, int>(p => p.Id);
        var a = new Person(1, "a");
        var b = new Person(1, "b");

        // when & then
        comparer.Equals(a, b).Should().BeTrue();
        comparer.GetHashCode(a).Should().Be(comparer.GetHashCode(b));
    }

    [Fact]
    public void should_treat_different_keys_as_not_equal_when_key_based_comparer()
    {
        // given
        var comparer = ComparerFactory.Create<Person, int>(p => p.Id);

        // when & then
        comparer.Equals(new Person(1, "a"), new Person(2, "a")).Should().BeFalse();
    }

    [Fact]
    public void should_handle_nulls_when_key_based_comparer()
    {
        // given
        var comparer = ComparerFactory.Create<Person, int>(p => p.Id);

        // when & then
        comparer.Equals(null, null).Should().BeTrue();
        comparer.Equals(new Person(1, "a"), null).Should().BeFalse();
        comparer.Equals(null, new Person(1, "a")).Should().BeFalse();
    }

    [Fact]
    public void should_compare_value_type_instances_by_key_when_key_based_comparer()
    {
        // given - value-type T exercises the no-boxing path (default(T) is not null)
        var comparer = ComparerFactory.Create<Point, int>(p => p.X);

        // when & then
        comparer.Equals(new Point(1, 2), new Point(1, 9)).Should().BeTrue();
        comparer.Equals(new Point(1, 2), new Point(3, 2)).Should().BeFalse();
        comparer.GetHashCode(new Point(1, 2)).Should().Be(comparer.GetHashCode(new Point(1, 5)));
    }

    [Fact]
    public void should_use_supplied_functions_when_comparison_func_comparer()
    {
        // given - compare strings case-insensitively
        var comparer = ComparerFactory.Create<string>(
            (x, y) => string.Equals(x, y, StringComparison.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase.GetHashCode
        );

        // when & then
        comparer.Equals("abc", "ABC").Should().BeTrue();
        comparer.Equals("abc", "xyz").Should().BeFalse();
        comparer.GetHashCode("abc").Should().Be(comparer.GetHashCode("ABC"));
    }

    [Fact]
    public void should_handle_nulls_without_invoking_func_when_comparison_func_comparer()
    {
        // given - the func must never run for null operands
        var comparer = ComparerFactory.Create<string>(
            (_, _) => throw new InvalidOperationException("comparison func should not be called for nulls"),
            x => x.Length
        );

        // when & then
        comparer.Equals(null, null).Should().BeTrue();
        comparer.Equals("a", null).Should().BeFalse();
        comparer.Equals(null, "a").Should().BeFalse();
    }

    private sealed record Person(int Id, string Name);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    private readonly record struct Point(int X, int Y);
}
