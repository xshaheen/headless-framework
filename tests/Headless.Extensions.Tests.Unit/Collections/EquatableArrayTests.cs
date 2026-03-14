// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Collections;

namespace Tests.Collections;

public sealed class EquatableArrayTests
{
    // Equals tests

    [Fact]
    public void equals_should_return_true_for_equal_arrays()
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
    public void equals_should_return_false_for_different_length()
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
    public void equals_should_return_false_for_different_elements()
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
    public void equals_should_return_true_for_both_empty()
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
    public void equals_object_should_return_true_for_equal_arrays()
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
    public void equals_object_should_return_false_for_non_equatable_array()
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
    public void equals_should_use_custom_comparer()
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
    public void get_hash_code_should_be_consistent_for_equal_arrays()
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
    public void get_hash_code_should_differ_for_different_arrays()
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
    public void get_hash_code_should_return_zero_for_null_array()
    {
        // given
        var array = new EquatableArray<int>(null!);

        // when
        var result = array.GetHashCode();

        // then
        result.Should().Be(0);
    }

    [Fact]
    public void get_hash_code_should_use_custom_comparer()
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
    public void equality_operator_should_return_true_for_equal_arrays()
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
    public void equality_operator_should_return_false_for_different_arrays()
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
    public void inequality_operator_should_return_false_for_equal_arrays()
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
    public void inequality_operator_should_return_true_for_different_arrays()
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
    public void as_span_should_return_span_of_underlying_array()
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
    public void as_array_should_return_underlying_array()
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
    public void count_should_return_length_of_array()
    {
        // given
        var array = new EquatableArray<int>([1, 2, 3]);

        // when
        var result = array.Count;

        // then
        result.Should().Be(3);
    }

    [Fact]
    public void count_should_return_zero_for_null_array()
    {
        // given
        var array = new EquatableArray<int>(null!);

        // when
        var result = array.Count;

        // then
        result.Should().Be(0);
    }

    [Fact]
    public void count_should_return_zero_for_empty_array()
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
    public void enumerable_should_iterate_over_elements()
    {
        // given
        var array = new EquatableArray<int>([1, 2, 3]);

        // when
        var result = array.ToList();

        // then
        result.Should().BeEquivalentTo([1, 2, 3]);
    }

    [Fact]
    public void enumerable_should_return_empty_for_null_array()
    {
        // given
        var array = new EquatableArray<int>(null!);

        // when
        var result = array.ToList();

        // then
        result.Should().BeEmpty();
    }

    [Fact]
    public void non_generic_enumerable_should_iterate_over_elements()
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
