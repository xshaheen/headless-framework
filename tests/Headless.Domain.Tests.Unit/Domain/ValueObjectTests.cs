// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;

namespace Tests.Domain;

public sealed class ValueObjectTests
{
    private sealed class Address : ValueObject<Address>
    {
        public required string Street { get; init; }

        public required string City { get; init; }

        protected override bool EqualityComponentsEqual(Address other) => Street == other.Street && City == other.City;

        protected override void BuildHashCode(ref HashCode hash)
        {
            hash.Add(Street);
            hash.Add(City);
        }
    }

    [Fact]
    public void should_implement_equality_base()
    {
        var address = new Address { Street = "123 Main St", City = "Springfield" };

        address.Should().BeAssignableTo<EqualityBase<Address>>();
    }

    [Fact]
    public void should_implement_i_value_object()
    {
        var address = new Address { Street = "123 Main St", City = "Springfield" };

        address.Should().BeAssignableTo<IValueObject>();
    }

    [Fact]
    public void should_compare_by_equality_components()
    {
        var address1 = new Address { Street = "123 Main St", City = "Springfield" };
        var address2 = new Address { Street = "123 Main St", City = "Springfield" };
        var address3 = new Address { Street = "456 Oak Ave", City = "Springfield" };

        address1.Should().Be(address2);
        address1.Should().NotBe(address3);
        address1.GetHashCode().Should().Be(address2.GetHashCode());
    }
}
