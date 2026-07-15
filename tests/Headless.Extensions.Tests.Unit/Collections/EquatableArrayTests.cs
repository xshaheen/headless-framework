// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Collections;

namespace Tests.Collections;

public sealed class EquatableArrayTests
{
    // Equals tests

    [Fact]
    public void should_return_true_for_equal_arrays_when_equals()
    {
        // given
        var array1 = new EquatableArray<int>([1, 2, 3]);
        var array2 = new EquatableArray<int>([1, 2, 3]);

        // when
        var result = array1.Equals(array2);

        // then
        result.Should().BeTrue();
    }

    [Fact]
    public void should_return_false_for_different_length_when_equals()
    {
        // given
        var array1 = new EquatableArray<int>([1, 2, 3]);
        var array2 = new EquatableArray<int>([1, 2]);

        // when
        var result = array1.Equals(array2);

        // then
        result.Should().BeFalse();
    }

    [Fact]
    public void should_return_false_for_different_elements_when_equals()
    {
        // given
        var array1 = new EquatableArray<int>([1, 2, 3]);
        var array2 = new EquatableArray<int>([1, 2, 4]);

        // when
        var result = array1.Equals(array2);

        // then
        result.Should().BeFalse();
    }

    [Fact]
    public void should_return_true_for_both_empty_when_equals()
    {
        // given
        var array1 = new EquatableArray<int>([]);
        var array2 = new EquatableArray<int>([]);

        // when
        var result = array1.Equals(array2);

        // then
        result.Should().BeTrue();
    }

    [Fact]
    public void should_return_true_for_equal_arrays_when_equals_object()
    {
        // given
        var array1 = new EquatableArray<int>([1, 2, 3]);
        object array2 = new EquatableArray<int>([1, 2, 3]);

        // when
        var result = array1.Equals(array2);

        // then
        result.Should().BeTrue();
    }

    [Fact]
    public void should_return_false_for_non_equatable_array_when_equals_object()
    {
        // given
        var array1 = new EquatableArray<int>([1, 2, 3]);
        object other = "not an array";

        // when
        var result = array1.Equals(other);

        // then
        result.Should().BeFalse();
    }

    [Fact]
    public void should_use_custom_comparer_when_equals()
    {
        // given
        var array1 = new EquatableArray<string>(["a", "b"], StringComparer.OrdinalIgnoreCase);
        var array2 = new EquatableArray<string>(["A", "B"], StringComparer.OrdinalIgnoreCase);

        // when
        var result = array1.Equals(array2);

        // then
        result.Should().BeTrue();
    }

    // GetHashCode tests

    [Fact]
    public void should_be_consistent_for_equal_arrays_when_get_hash_code()
    {
        // given
        var array1 = new EquatableArray<int>([1, 2, 3]);
        var array2 = new EquatableArray<int>([1, 2, 3]);

        // when
        var hash1 = array1.GetHashCode();
        var hash2 = array2.GetHashCode();

        // then
        hash1.Should().Be(hash2);
    }

    [Fact]
    public void should_differ_for_different_arrays_when_get_hash_code()
    {
        // given
        var array1 = new EquatableArray<int>([1, 2, 3]);
        var array2 = new EquatableArray<int>([1, 2, 4]);

        // when
        var hash1 = array1.GetHashCode();
        var hash2 = array2.GetHashCode();

        // then
        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void should_return_zero_for_null_array_when_get_hash_code()
    {
        // given
        var array = new EquatableArray<int>(null!);

        // when
        var result = array.GetHashCode();

        // then
        result.Should().Be(0);
    }

    [Fact]
    public void should_use_custom_comparer_when_get_hash_code()
    {
        // given
        var array1 = new EquatableArray<string>(["a", "b"], StringComparer.OrdinalIgnoreCase);
        var array2 = new EquatableArray<string>(["A", "B"], StringComparer.OrdinalIgnoreCase);

        // when
        var hash1 = array1.GetHashCode();
        var hash2 = array2.GetHashCode();

        // then
        hash1.Should().Be(hash2);
    }

    // Operator tests

    [Fact]
    public void should_return_true_for_equal_arrays_when_equality_operator()
    {
        // given
        var array1 = new EquatableArray<int>([1, 2, 3]);
        var array2 = new EquatableArray<int>([1, 2, 3]);

        // when
        var result = array1 == array2;

        // then
        result.Should().BeTrue();
    }

    [Fact]
    public void should_return_false_for_different_arrays_when_equality_operator()
    {
        // given
        var array1 = new EquatableArray<int>([1, 2, 3]);
        var array2 = new EquatableArray<int>([1, 2, 4]);

        // when
        var result = array1 == array2;

        // then
        result.Should().BeFalse();
    }

    [Fact]
    public void should_return_false_for_equal_arrays_when_inequality_operator()
    {
        // given
        var array1 = new EquatableArray<int>([1, 2, 3]);
        var array2 = new EquatableArray<int>([1, 2, 3]);

        // when
        var result = array1 != array2;

        // then
        result.Should().BeFalse();
    }

    [Fact]
    public void should_return_true_for_different_arrays_when_inequality_operator()
    {
        // given
        var array1 = new EquatableArray<int>([1, 2, 3]);
        var array2 = new EquatableArray<int>([1, 2, 4]);

        // when
        var result = array1 != array2;

        // then
        result.Should().BeTrue();
    }

    // AsSpan tests

    [Fact]
    public void should_return_span_of_underlying_array_when_as_span()
    {
        // given
        var array = new EquatableArray<int>([1, 2, 3]);

        // when
        var span = array.AsSpan();

        // then
        span.Length.Should().Be(3);
        span[0].Should().Be(1);
        span[1].Should().Be(2);
        span[2].Should().Be(3);
    }

    // AsArray tests

    [Fact]
    public void should_return_underlying_array_when_as_array()
    {
        // given
        int[] original = [1, 2, 3];
        var equatableArray = new EquatableArray<int>(original);

        // when
        var result = equatableArray.AsArray();

        // then
        result.Should().BeSameAs(original);
    }

    // Count tests

    [Fact]
    public void should_return_length_of_array_when_count()
    {
        // given
        var array = new EquatableArray<int>([1, 2, 3]);

        // when
        var result = array.Count;

        // then
        result.Should().Be(3);
    }

    [Fact]
    public void should_return_zero_for_null_array_when_count()
    {
        // given
        var array = new EquatableArray<int>(null!);

        // when
        var result = array.Count;

        // then
        result.Should().Be(0);
    }

    [Fact]
    public void should_return_zero_for_empty_array_when_count()
    {
        // given
        var array = new EquatableArray<int>([]);

        // when
        var result = array.Count;

        // then
        result.Should().Be(0);
    }

    // IEnumerable tests

    [Fact]
    public void should_iterate_over_elements_when_enumerable()
    {
        // given
        var array = new EquatableArray<int>([1, 2, 3]);

        // when
        var result = array.ToList();

        // then
        result.Should().BeEquivalentTo([1, 2, 3]);
    }

    [Fact]
    public void should_return_empty_for_null_array_when_enumerable()
    {
        // given
        var array = new EquatableArray<int>(null!);

        // when
        var result = array.ToList();

        // then
        result.Should().BeEmpty();
    }

    [Fact]
    public void should_iterate_over_elements_when_non_generic_enumerable()
    {
        // given
        var array = new EquatableArray<int>([1, 2, 3]);
        var enumerable = (System.Collections.IEnumerable)array;

        // when
        var result = new List<int>();
        foreach (var item in enumerable)
        {
            result.Add((int)item);
        }

        // then
        result.Should().BeEquivalentTo([1, 2, 3]);
    }
}
