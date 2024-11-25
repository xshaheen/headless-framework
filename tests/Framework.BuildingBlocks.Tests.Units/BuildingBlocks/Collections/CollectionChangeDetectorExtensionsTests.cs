// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests.BuildingBlocks.Collections;

public class CollectionChangeDetectorExtensionsTests
{
    public class Person
    {
        public required int Id { get; set; }

        public required string Name { get; set; }
    }

    [Fact]
    public void DetectChanges_ShouldIdentifyAddedRemovedAndExistItems()
    {
        // Arrange
        var oldItems = new List<Person>
        {
            new() { Id = 1, Name = "Alice" },
            new() { Id = 2, Name = "Bob" },
            new() { Id = 3, Name = "Charlie" },
        };

        var newItems = new List<Person>
        {
            new() { Id = 2, Name = "Bob" },
            new() { Id = 3, Name = "Charlie" },
            new() { Id = 4, Name = "David" },
        };

        // Act
        var (addedItems, removedItems, existItems) = oldItems.DetectChanges(newItems, (x, y) => x.Id == y.Id);

        // Assert
        addedItems
            .Should()
            .BeEquivalentTo(
                new List<Person>
                {
                    new() { Id = 4, Name = "David" },
                }
            );

        removedItems
            .Should()
            .BeEquivalentTo(
                new List<Person>
                {
                    new() { Id = 1, Name = "Alice" },
                }
            );

        existItems
            .Should()
            .BeEquivalentTo(
                new List<(Person, Person)>
                {
                    (new Person { Id = 2, Name = "Bob" }, new Person { Id = 2, Name = "Bob" }),
                    (new Person { Id = 3, Name = "Charlie" }, new Person { Id = 3, Name = "Charlie" }),
                }
            );
    }

    [Fact]
    public void DetectChanges_WithChangeDetection_ShouldIdentifyAddedRemovedUpdatedAndSameItems()
    {
        // Arrange
        var oldItems = new List<Person>
        {
            new() { Id = 1, Name = "Alice" },
            new() { Id = 2, Name = "Bob" },
            new() { Id = 3, Name = "Charlie" },
        };

        var newItems = new List<Person>
        {
            new() { Id = 2, Name = "Bob" },
            new() { Id = 3, Name = "Charlie Updated" },
            new() { Id = 4, Name = "David" },
        };

        // Act
        var (addedItems, removedItems, updatedItems, sameItems) = oldItems.DetectChanges(
            newItems,
            (x, y) => x.Id == y.Id,
            (x, y) => !string.Equals(x.Name, y.Name, StringComparison.Ordinal)
        );

        // Assert
        addedItems
            .Should()
            .BeEquivalentTo(
                new List<Person>
                {
                    new() { Id = 4, Name = "David" },
                }
            );

        removedItems
            .Should()
            .BeEquivalentTo(
                new List<Person>
                {
                    new() { Id = 1, Name = "Alice" },
                }
            );

        updatedItems
            .Should()
            .BeEquivalentTo(
                new List<(Person, Person)>
                {
                    (new Person { Id = 3, Name = "Charlie" }, new Person { Id = 3, Name = "Charlie Updated" }),
                }
            );

        sameItems
            .Should()
            .BeEquivalentTo(
                new List<(Person, Person)>
                {
                    (new Person { Id = 2, Name = "Bob" }, new Person { Id = 2, Name = "Bob" }),
                }
            );
    }
}
