// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Kernel.Domains;

namespace Tests.Helpers;

public sealed class EqualityBaseTests
{
    private sealed class IdEqualityItem : EqualityBase<IdEqualityItem>
    {
        public Guid Id { get; init; }

        public string? Description { get; init; }

        protected override IEnumerable<object?> EqualityComponents()
        {
            yield return Id;
        }
    }

    [Fact]
    public void should_compare_based_on_yielded_items_only()
    {
        var id = Guid.NewGuid();
        var item1 = new IdEqualityItem { Id = id, Description = "Item 1" };
        var item2 = new IdEqualityItem { Id = id, Description = "Item 2" };

        item1.Equals(item2).Should().BeTrue();
        (item1 == item2).Should().BeTrue();
        (item1 != item2).Should().BeFalse();
    }
}
