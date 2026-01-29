// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;

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

    private sealed class CompositeEqualityItem : EqualityBase<CompositeEqualityItem>
    {
        public string? Name { get; init; }

        public int Value { get; init; }

        protected override IEnumerable<object?> EqualityComponents()
        {
            yield return Name;
            yield return Value;
        }
    }

    private sealed class AnotherEqualityItem : EqualityBase<AnotherEqualityItem>
    {
        public Guid Id { get; init; }

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

    [Fact]
    public void should_return_false_when_compared_to_null()
    {
        var item = new IdEqualityItem { Id = Guid.NewGuid() };

        item.Equals(null).Should().BeFalse();
    }

    [Fact]
    public void should_return_true_when_reference_equal()
    {
        var item = new IdEqualityItem { Id = Guid.NewGuid() };

        item.Equals(item).Should().BeTrue();
    }

    [Fact]
    public void should_return_false_when_different_component_values()
    {
        var item1 = new IdEqualityItem { Id = Guid.NewGuid() };
        var item2 = new IdEqualityItem { Id = Guid.NewGuid() };

        item1.Equals(item2).Should().BeFalse();
    }

    [Fact]
    public void should_handle_null_equality_components()
    {
        var item1 = new CompositeEqualityItem { Name = null, Value = 42 };
        var item2 = new CompositeEqualityItem { Name = null, Value = 42 };

        item1.Equals(item2).Should().BeTrue();
        item1.GetHashCode().Should().Be(item2.GetHashCode());
    }

    [Fact]
    public void should_compute_consistent_hash_code()
    {
        var id = Guid.NewGuid();
        var item1 = new IdEqualityItem { Id = id };
        var item2 = new IdEqualityItem { Id = id };

        item1.GetHashCode().Should().Be(item2.GetHashCode());
    }

    [Fact]
    public void should_compute_different_hash_for_different_values()
    {
        var item1 = new IdEqualityItem { Id = Guid.NewGuid() };
        var item2 = new IdEqualityItem { Id = Guid.NewGuid() };

        item1.GetHashCode().Should().NotBe(item2.GetHashCode());
    }

    [Fact]
    public void should_support_operator_equals()
    {
        var id = Guid.NewGuid();
        var item1 = new IdEqualityItem { Id = id };
        var item2 = new IdEqualityItem { Id = id };

        (item1 == item2).Should().BeTrue();
    }

    [Fact]
    public void should_support_operator_not_equals()
    {
        var item1 = new IdEqualityItem { Id = Guid.NewGuid() };
        var item2 = new IdEqualityItem { Id = Guid.NewGuid() };

        (item1 != item2).Should().BeTrue();
    }

    [Fact]
    public void should_handle_both_null_with_operator()
    {
        IdEqualityItem? left = null;
        IdEqualityItem? right = null;

        (left == right).Should().BeTrue();
        (left != right).Should().BeFalse();
    }

    [Fact]
    public void should_handle_left_null_with_operator()
    {
        IdEqualityItem? left = null;
        var right = new IdEqualityItem { Id = Guid.NewGuid() };

        (left == right).Should().BeFalse();
        (left != right).Should().BeTrue();
    }

    [Fact]
    public void should_handle_right_null_with_operator()
    {
        var left = new IdEqualityItem { Id = Guid.NewGuid() };
        IdEqualityItem? right = null;

        (left == right).Should().BeFalse();
        (left != right).Should().BeTrue();
    }

    [Fact]
    public void should_return_true_with_object_equals_when_equal()
    {
        var id = Guid.NewGuid();
        var item1 = new IdEqualityItem { Id = id };
        object item2 = new IdEqualityItem { Id = id };

        item1.Equals(item2).Should().BeTrue();
    }

    [Fact]
    public void should_return_false_with_object_equals_when_different_type()
    {
        var id = Guid.NewGuid();
        var item1 = new IdEqualityItem { Id = id };
        object item2 = "not an IdEqualityItem";

        item1.Equals(item2).Should().BeFalse();
    }
}
