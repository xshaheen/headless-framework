// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests.Extensions.Collections;

public sealed class CollectionChangeDetectorExtensionsTests
{
    private sealed record ItemDetectorExtensionsTTest(int Id, string Value);

    // // TODO change to accept reference and value type
    // [Fact]
    // public void detect_changes_should_correctly_identify_added_removed_and_existing_items()
    // {
    //     // Arrange
    //     var oldItems = new List<int> { 1, 2, 3 };
    //     var newItems = new List<int> { 3, 4, 5 };
    //
    //     Func<int, int, bool> areSameitem = (oldItem, newItem) => oldItem == newItem;
    //
    //     // Act
    //     var result = oldItems.DetectChanges(newItems, areSameitem);
    //
    //     // Assert
    //     result.AddedItems.Should().BeEquivalentTo(new List<int> { 4, 5 });
    //     result.RemovedItems.Should().BeEquivalentTo(new List<int> { 1, 2 });
    //     result.ExistItems.Should().BeEquivalentTo(new List<(int, int)> { (3, 3) });
    // }


    [Fact]
    public void detect_changes_with_update_logic_should_identify_added_removed_updated_and_same_items()
    {
        // given
        var oldItems = new List<ItemDetectorExtensionsTTest>
        {
            new(1, "A"),
            new(2, "B"),
            new(3, "C"),
        };

        var newItems = new List<ItemDetectorExtensionsTTest>
        {
            new(3, "C"),
            new(4, "D"),
            new(2, "Updated B")
        };

        Func<ItemDetectorExtensionsTTest, ItemDetectorExtensionsTTest, bool> areSameEntity = (oldItem, newItem) => oldItem.Id == newItem.Id;
        Func<ItemDetectorExtensionsTTest, ItemDetectorExtensionsTTest, bool> hasChange = (oldItem, newItem) => oldItem.Value != newItem.Value;

        // when
        var result = oldItems.DetectChanges(newItems, areSameEntity, hasChange);

        // then
        result.AddedItems.Should().BeEquivalentTo(
            new List<ItemDetectorExtensionsTTest>
            {
                new(4, "D"),
            }
        );

        result.RemovedItems.Should().BeEquivalentTo(
            new List<ItemDetectorExtensionsTTest>
            {
                new(1, "A"),
            }
        );

        result.UpdatedItems.Should().BeEquivalentTo(
            new List<(ItemDetectorExtensionsTTest, ItemDetectorExtensionsTTest)>
            {
                (
                    new(2, "B"),
                    new(2, "Updated B")
                ),
            }
        );

        result.SameItems.Should().BeEquivalentTo(
            new List<(ItemDetectorExtensionsTTest, ItemDetectorExtensionsTTest)>
            {
                (
                    new(3, "C"),
                    new(3, "C")
                ),
            }
        );
    }

    [Fact]
    public void detect_updates_should_identify_updated_and_same_items_correctly()
    {
        // given
        var existItems = new List<(ItemDetectorExtensionsTTest, ItemDetectorExtensionsTTest)>
        {
            (
                new(1, "A"),
                new(1, "Updated A")
            ),
            (
                new(2, "B"),
                new(2, "B")
            ),
        };

        Func<ItemDetectorExtensionsTTest, ItemDetectorExtensionsTTest, bool> hasChange = (oldItem, newItem) => oldItem.Value != newItem.Value;

        // when
        var result = existItems.DetectUpdates(hasChange);

        // then
        result.UpdatedItems.Should().BeEquivalentTo(
            new List<(ItemDetectorExtensionsTTest, ItemDetectorExtensionsTTest)>
            {
                (
                    new(1, "A"),
                    new(1, "Updated A")
                ),
            }
        );

        result.SameItems.Should().BeEquivalentTo(
            new List<(ItemDetectorExtensionsTTest, ItemDetectorExtensionsTTest)>
            {
                (
                    new(2, "B"),
                    new(2, "B")
                ),
            }
        );
    }
}
