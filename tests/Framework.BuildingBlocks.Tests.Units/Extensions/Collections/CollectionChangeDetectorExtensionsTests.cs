// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests.Extensions.Collections;

public sealed class CollectionChangeDetectorExtensionsTests
{
    [Fact]
    public void detect_updates_should_identify_updated_and_same_items_correctly()
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
    public void detect_changes_with_update_logic_should_identify_added_removed_updated_and_same_items()
    {
        // given
        var oldItems = new List<KeyValue> { new(1, "A"), new(2, "B"), new(3, "C") };
        var newItems = new List<KeyValue> { new(3, "C"), new(4, "D"), new(2, "Updated B") };

        // when
        var (addedItems, removedItems, updatedItems, sameItems) = oldItems.DetectChanges(
            newItems,
            areSameEntity: (oldItem, newItem) => oldItem.Id == newItem.Id,
            hasChange: (oldItem, newItem) => !string.Equals(oldItem.Value, newItem.Value, StringComparison.Ordinal)
        );

        // then
        addedItems.Should().BeEquivalentTo([new KeyValue(4, "D")]);
        removedItems.Should().BeEquivalentTo([new KeyValue(1, "A")]);
        updatedItems.Should().BeEquivalentTo([(new KeyValue(2, "B"), new KeyValue(2, "Updated B"))]);
        sameItems.Should().BeEquivalentTo([(new KeyValue(3, "C"), new KeyValue(3, "C"))]);
    }

    [Fact]
    public void detect_changes_with_update_logic_should_identify_added_removed_updated_and_exists_items()
    {
        // given
        List<KeyValue> oldItems = [new(1, "Alice"), new(2, "Bob"), new(3, "Charlie")];
        List<KeyValue> newItems = [new(2, "Bob"), new(3, "Charlie"), new(4, "David")];

        // when
        var (addedItems, removedItems, existItems) = oldItems.DetectChanges(newItems, (x, y) => x.Id == y.Id);

        // then
        addedItems.Should().BeEquivalentTo([new KeyValue(4, "David")]);
        removedItems.Should().BeEquivalentTo([new KeyValue(1, "Alice")]);

        existItems
            .Should()
            .BeEquivalentTo(
                [
                    (new KeyValue(2, "Bob"), new KeyValue(2, "Bob")),
                    (new KeyValue(3, "Charlie"), new KeyValue(3, "Charlie")),
                ]
            );
    }

    private sealed record KeyValue(int Id, string Value, string[]? Skills = null);
}
