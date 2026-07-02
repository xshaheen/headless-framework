// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests.Collections;

public sealed class ListExtensionsTests
{
    [Fact]
    public void InsertRange_tests()
    {
        var list = Enumerable.Range(1, 3).ToList();
        list.InsertRange(1, [7, 8, 9]);

        list.Should().HaveElementAt(0, 1);
        list.Should().HaveElementAt(1, 7);
        list.Should().HaveElementAt(2, 8);
        list.Should().HaveElementAt(3, 9);
        list.Should().HaveElementAt(4, 2);
        list.Should().HaveElementAt(5, 3);
    }

    [Fact]
    public void InsertAfter_tests()
    {
        var list = Enumerable.Range(1, 3).ToList();

        list.InsertAfter(2, 42);

        list.Should().HaveCount(4);
        list.Should().HaveElementAt(0, 1);
        list.Should().HaveElementAt(1, 2);
        list.Should().HaveElementAt(2, 42);
        list.Should().HaveElementAt(3, 3);

        list.InsertAfter(3, 43);

        list.Should().HaveCount(5);
        list.Should().HaveElementAt(0, 1);
        list.Should().HaveElementAt(1, 2);
        list.Should().HaveElementAt(2, 42);
        list.Should().HaveElementAt(3, 3);
        list.Should().HaveElementAt(4, 43);
    }

    [Fact]
    public void InsertAfter_with_predicate_tests()
    {
        var list = Enumerable.Range(1, 3).ToList();

        list.InsertAfter(i => i == 2, 42);

        list.Should().HaveCount(4);
        list.Should().HaveElementAt(0, 1);
        list.Should().HaveElementAt(1, 2);
        list.Should().HaveElementAt(2, 42);
        list.Should().HaveElementAt(3, 3);

        list.InsertAfter(i => i == 3, 43);

        list.Should().HaveCount(5);
        list.Should().HaveElementAt(0, 1);
        list.Should().HaveElementAt(1, 2);
        list.Should().HaveElementAt(2, 42);
        list.Should().HaveElementAt(3, 3);
        list.Should().HaveElementAt(4, 43);
    }

    [Fact]
    public void InsertAfter_with_predicate__should_insert_to_first_if_not_found()
    {
        var list = Enumerable.Range(1, 3).ToList();

        list.InsertAfter(i => i == 999, 42);

        list.Should().HaveCount(4);
        list.Should().HaveElementAt(0, 42);
        list.Should().HaveElementAt(1, 1);
        list.Should().HaveElementAt(2, 2);
        list.Should().HaveElementAt(3, 3);
    }

    [Fact]
    public void InsertBefore_tests()
    {
        var list = Enumerable.Range(1, 3).ToList();

        list.InsertBefore(2, 42);

        list.Should().HaveCount(4);
        list.Should().HaveElementAt(0, 1);
        list.Should().HaveElementAt(1, 42);
        list.Should().HaveElementAt(2, 2);
        list.Should().HaveElementAt(3, 3);

        list.InsertBefore(1, 43);

        list.Should().HaveCount(5);
        list.Should().HaveElementAt(0, 43);
        list.Should().HaveElementAt(1, 1);
        list.Should().HaveElementAt(2, 42);
        list.Should().HaveElementAt(3, 2);
        list.Should().HaveElementAt(4, 3);
    }

    [Fact]
    public void InsertBefore_with_predicate_tests()
    {
        var list = Enumerable.Range(1, 3).ToList();

        list.InsertBefore(i => i == 2, 42);

        list.Should().HaveCount(4);
        list.Should().HaveElementAt(0, 1);
        list.Should().HaveElementAt(1, 42);
        list.Should().HaveElementAt(2, 2);
        list.Should().HaveElementAt(3, 3);

        list.InsertBefore(i => i == 1, 43);

        list.Should().HaveCount(5);
        list.Should().HaveElementAt(0, 43);
        list.Should().HaveElementAt(1, 1);
        list.Should().HaveElementAt(2, 42);
        list.Should().HaveElementAt(3, 2);
        list.Should().HaveElementAt(4, 3);
    }

    [Fact]
    public void ReplaceWhile_with_value_tests()
    {
        var list = Enumerable.Range(1, 3).ToList();

        list.ReplaceWhile(i => i >= 2, 42);

        list.Should().HaveElementAt(0, 1);
        list.Should().HaveElementAt(1, 42);
        list.Should().HaveElementAt(2, 42);
    }

    [Fact]
    public void ReplaceWhile_with_factory_tests()
    {
        var list = Enumerable.Range(1, 3).ToList();

        list.ReplaceWhile(i => i >= 2, i => i + 1);

        list.Should().HaveElementAt(0, 1);
        list.Should().HaveElementAt(1, 3);
        list.Should().HaveElementAt(2, 4);
    }

    [Fact]
    public void ReplaceFirst_with_value_tests()
    {
        var list = Enumerable.Range(1, 3).ToList();

        list.ReplaceFirst(i => i >= 2, 42);

        list.Should().HaveElementAt(0, 1);
        list.Should().HaveElementAt(1, 42);
        list.Should().HaveElementAt(2, 3);
    }

    [Fact]
    public void ReplaceFirst_with_factory_tests()
    {
        var list = Enumerable.Range(1, 3).ToList();

        list.ReplaceFirst(i => i >= 2, i => i + 1);

        list.Should().HaveElementAt(0, 1);
        list.Should().HaveElementAt(1, 3);
        list.Should().HaveElementAt(2, 3);
    }

    [Fact]
    public void ReplaceFirst_with_item_tests()
    {
        var list = Enumerable.Range(1, 3).ToList();

        list.ReplaceFirst(2, 42);

        list.Should().HaveElementAt(0, 1);
        list.Should().HaveElementAt(1, 42);
        list.Should().HaveElementAt(2, 3);
    }

    [Fact]
    public void ReplaceFirst_with_item_should_replace_non_comparable_reference_type()
    {
        // given - records are not IComparable; the old Comparer<T>.Default.Compare path threw for them.
        var a = new Box(1);
        var c = new Box(3);
        var list = new List<Box> { a, new(2), c };

        // when
        list.ReplaceFirst(new Box(2), new Box(42));

        // then
        list.Should().Equal(a, new Box(42), c);
    }

    [Fact]
    public void GetOrAdd_should_add_when_no_value_type_element_matches()
    {
        // given - regression: the old FirstOrDefault + "is not null" check treated a value-type default
        // as a hit, so a non-matching selector returned default(int) and never appended a new element.
        var list = new List<int> { 1, 2, 3 };

        // when
        var result = list.GetOrAdd(x => x == 99, () => 42);

        // then
        result.Should().Be(42);
        list.Should().Equal(1, 2, 3, 42);
    }

    [Fact]
    public void GetOrAdd_should_return_existing_value_type_match_without_adding()
    {
        // given
        var list = new List<int> { 1, 2, 3 };

        // when
        var result = list.GetOrAdd(x => x == 2, () => 42);

        // then
        result.Should().Be(2);
        list.Should().Equal(1, 2, 3);
    }

    [Fact]
    public void MoveItem_should_move_matching_element_to_target_index()
    {
        // given
        var list = new List<int> { 1, 2, 3, 4 };

        // when
        list.MoveItem(x => x == 4, 0);

        // then
        list.Should().Equal(4, 1, 2, 3);
    }

    [Fact]
    public void MoveItem_should_throw_when_no_element_matches()
    {
        // given
        var list = new List<int> { 1, 2, 3 };

        // when
        var act = () => list.MoveItem(x => x == 99, 0);

        // then
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private sealed record Box(int Value);
}
