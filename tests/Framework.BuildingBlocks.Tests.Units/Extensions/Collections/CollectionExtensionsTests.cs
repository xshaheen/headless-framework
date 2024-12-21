// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests.Extensions.Collections;

public sealed class CollectionExtensionsTests
{
    [Fact]
    public void add_if_not_contains_with_predicate()
    {
        List<int> collection = [4, 5, 6];

        collection.AddIfNotContains(x => x == 5, () => 5);
        collection.Should().HaveCount(3);

        collection.AddIfNotContains(x => x == 42, () => 42);
        collection.Should().HaveCount(4);

        collection.AddIfNotContains(x => x < 8, () => 8);
        collection.Should().HaveCount(4);

        collection.AddIfNotContains(x => x > 999, () => 8);
        collection.Should().HaveCount(5);
    }

    [Fact]
    public void add_range_list_adds_items()
    {
        // given
        var list = new List<int> { 1, 2 };
        var values = new[] { 3, 4, 5 };

        // when
        list.AddRange(values);

        // then
        list.Should().HaveCount(5).And.Contain([3, 4, 5]);
    }

    [Fact]
    public void add_range_set_adds_items()
    {
        // given
        var set = new HashSet<int> { 1, 2 };
        var values = new[] { 2, 3, 4 };

        // when
        set.AddRange(values);

        // then
        set.Should().HaveCount(4).And.Contain([1, 2, 3, 4]);
    }

    [Fact]
    public void add_range_generic_collection_adds_items()
    {
        // given
        ICollection<int> collection = [1, 2];
        var values = new[] { 3, 4 };

        // when
        collection.AddRange(values);

        // then
        collection.Should().HaveCount(4).And.Contain([1, 2, 3, 4]);
    }

    [Fact]
    public void is_null_or_empty_null_collection_returns_true()
    {
        // given
        List<int>? collection = null;

        // when & then
        collection.IsNullOrEmpty().Should().BeTrue();
    }

    [Fact]
    public void is_null_or_empty_empty_collection_returns_true()
    {
        // given
        var collection = new List<int>();

        // then & when
        collection.IsNullOrEmpty().Should().BeTrue();
    }

    [Fact]
    public void is_null_or_empty_non_empty_collection_returns_false()
    {
        // give
        var collection = new List<int> { 1, 2, 2 };

        // when & then
        collection.IsNullOrEmpty().Should().BeFalse();
    }

    [Fact]
    public void add_if_not_contains_adds_unique_item()
    {
        // given
        var list = new List<int> { 1, 2, 3 };

        // when
        var added = list.AddIfNotContains(4);

        // then
        added.Should().BeTrue();
        list.Should().Contain(4);
    }

    [Fact]
    public void add_if_not_contains_does_not_add_duplicate_item()
    {
        // given
        var list = new List<int> { 1, 2, 3 };

        // when
        var added = list.AddIfNotContains(2);

        // then
        added.Should().BeFalse();
        list.Should().HaveCount(3);
    }

    [Fact]
    public void add_if_not_contains_with_items_adds_unique_items()
    {
        // given
        var list = new List<int> { 1, 2, 3 };
        var items = new[] { 2, 3, 4 };
        // when
        var addedItems = list.AddIfNotContains(items);
        // then
        addedItems.Should().BeEquivalentTo([4]);
        list.Should().HaveCount(4).And.Contain(4);
    }

    [Fact]
    public void remove_all_with_predicate_removes_matching_items()
    {
        // given
        ICollection<int> list = [1, 2, 3, 4];
        // when
        list.RemoveAll(x => x % 2 == 0);
        // then
        list.Should().BeEquivalentTo([1, 3]);
    }

    [Fact]
    public void remove_all_removes_specified_items()
    {
        // given
        var list = new List<int> { 1, 2, 3, 4 };
        var itemsToRemove = new[] { 2, 3 };
        // when
        list.RemoveAll(itemsToRemove);
        // then
        list.Should().BeEquivalentTo([1, 4]);
    }
}
