// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests.Collections;

public sealed class CollectionChangeDetectorExtensionsTests
{
    [Fact]
    public void should_identify_updated_and_same_items_correctly_when_detect_updates()
    {
        // given
        var existItems = new List<(KeyValue, KeyValue)>
        {
            (new(1, "A"), new(1, "Updated A")),
            (new(2, "B"), new(2, "B")),
        };

        // when
        var (updatedItems, sameItems) = existItems.DetectUpdates(
            (item1, item2) => !string.Equals(item1.Value, item2.Value, StringComparison.Ordinal)
        );

        // then
        updatedItems.Should().BeEquivalentTo([(new KeyValue(1, "A"), new KeyValue(1, "Updated A"))]);
        sameItems.Should().BeEquivalentTo([(new KeyValue(2, "B"), new KeyValue(2, "B"))]);
    }

    [Fact]
    public void should_identify_added_removed_updated_and_same_items_when_detect_changes_with_update_logic()
    {
        // given
        var oldItems = new List<KeyValue> { new(1, "A"), new(2, "B"), new(3, "C") };
        var newItems = new List<KeyValue> { new(3, "C"), new(4, "D"), new(2, "Updated B") };

        // when
        var (addedItems, removedItems, updatedItems, sameItems) = oldItems.DetectChanges(
            newItems,
            oldKeySelector: oldItem => oldItem.Id,
            newKeySelector: newItem => newItem.Id,
            hasChange: (oldItem, newItem) => !string.Equals(oldItem.Value, newItem.Value, StringComparison.Ordinal)
        );

        // then
        addedItems.Should().BeEquivalentTo([new KeyValue(4, "D")]);
        removedItems.Should().BeEquivalentTo([new KeyValue(1, "A")]);
        updatedItems.Should().BeEquivalentTo([(new KeyValue(2, "B"), new KeyValue(2, "Updated B"))]);
        sameItems.Should().BeEquivalentTo([(new KeyValue(3, "C"), new KeyValue(3, "C"))]);
    }

    [Fact]
    public void should_identify_added_removed_and_exist_items_when_detect_changes()
    {
        // given
        List<KeyValue> oldItems = [new(1, "Alice"), new(2, "Bob"), new(3, "Charlie")];
        List<KeyValue> newItems = [new(2, "Bob"), new(3, "Charlie"), new(4, "David")];

        // when
        var (addedItems, removedItems, existItems) = oldItems.DetectChanges(newItems, x => x.Id, y => y.Id);

        // then
        addedItems.Should().BeEquivalentTo([new KeyValue(4, "David")]);
        removedItems.Should().BeEquivalentTo([new KeyValue(1, "Alice")]);

        existItems
            .Should()
            .BeEquivalentTo([
                (new KeyValue(2, "Bob"), new KeyValue(2, "Bob")),
                (new KeyValue(3, "Charlie"), new KeyValue(3, "Charlie")),
            ]);
    }

    [Fact]
    public void should_classify_value_type_elements_without_misclassification_when_detect_changes()
    {
        // given - value-type elements: the previous `FirstOrDefault(...) is null` match misclassified these.
        int[] oldItems = [1, 2, 3];
        int[] newItems = [2, 3, 4];

        // when - identity key
        var (added, removed, exist) = oldItems.DetectChanges(newItems, oldItem => oldItem, newItem => newItem);

        // then
        added.Should().BeEquivalentTo([4]);
        removed.Should().BeEquivalentTo([1]);
        exist.Should().BeEquivalentTo([(2, 2), (3, 3)]);
    }

    [Fact]
    public void should_match_across_different_old_and_new_types_when_detect_changes()
    {
        // given - old items are raw ids, new items are entities keyed by Id
        int[] oldIds = [1, 2];
        List<KeyValue> newItems = [new(2, "B"), new(3, "C")];

        // when
        var (added, removed, exist) = oldIds.DetectChanges(newItems, oldId => oldId, item => item.Id);

        // then
        added.Should().BeEquivalentTo([new KeyValue(3, "C")]);
        removed.Should().BeEquivalentTo([1]);
        exist.Should().BeEquivalentTo([(2, new KeyValue(2, "B"))]);
    }

    [Fact]
    public void should_match_keys_using_the_supplied_comparer_when_detect_changes()
    {
        // given - keys differ only by case
        List<KeyValue> oldItems = [new(1, "alice")];
        List<KeyValue> newItems = [new(2, "ALICE")];

        // when - keyed on Value with a case-insensitive comparer
        var (added, removed, exist) = oldItems.DetectChanges(
            newItems,
            oldItem => oldItem.Value,
            newItem => newItem.Value,
            StringComparer.OrdinalIgnoreCase
        );

        // then - matched as the same entity despite case
        added.Should().BeEmpty();
        removed.Should().BeEmpty();
        exist.Should().ContainSingle();
    }

    [Fact]
    public void should_treat_all_as_added_when_detect_changes_old_is_empty()
    {
        // given
        List<KeyValue> oldItems = [];
        List<KeyValue> newItems = [new(1, "A"), new(2, "B")];

        // when
        var (added, removed, exist) = oldItems.DetectChanges(newItems, x => x.Id, y => y.Id);

        // then
        added.Should().HaveCount(2);
        removed.Should().BeEmpty();
        exist.Should().BeEmpty();
    }

    [Fact]
    public void should_treat_all_as_removed_when_detect_changes_new_is_empty()
    {
        // given
        List<KeyValue> oldItems = [new(1, "A"), new(2, "B")];
        List<KeyValue> newItems = [];

        // when
        var (added, removed, exist) = oldItems.DetectChanges(newItems, x => x.Id, y => y.Id);

        // then
        added.Should().BeEmpty();
        removed.Should().HaveCount(2);
        exist.Should().BeEmpty();
    }

    private sealed record KeyValue(int Id, string Value, string[]? Skills = null);
}
