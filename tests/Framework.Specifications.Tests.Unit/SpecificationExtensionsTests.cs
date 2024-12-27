// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Specifications;
using Tests.Models;

namespace Tests;

public class SpecificationExtensionsTests
{
    private readonly List<Product> _products =
    [
        new Product
        {
            Name = "Red Chair",
            Price = 49.99m,
            Category = "Furniture",
            Stock = 5,
            Color = Color.Red,
            Available = false,
        },
        new Product
        {
            Name = "Green Table",
            Price = 89.99m,
            Category = "Furniture",
            Stock = 0,
            Color = Color.Green,
            Available = false,
        },
        new Product
        {
            Name = "Black Sofa",
            Price = 299.99m,
            Category = "Furniture",
            Stock = 2,
            Color = Color.Black,
            Available = false,
        },
        new Product
        {
            Name = "White Lamp",
            Price = 19.99m,
            Category = "Lighting",
            Stock = 10,
            Color = Color.White,
            Available = false,
        },
        new Product
        {
            Name = "Red Mug",
            Price = 9.99m,
            Category = "Kitchenware",
            Stock = 15,
            Color = Color.Red,
            Available = true,
        },
        new Product
        {
            Name = "Green Plate",
            Price = 4.99m,
            Category = "Kitchenware",
            Stock = 20,
            Color = Color.Green,
            Available = false,
        },
    ];

    [Fact]
    public void and_should_return_elements_satisfying_both_specifications()
    {
        // given
        var colorSpec = new ExpressionSpecification<Product>(p => p.Color == Color.Red);
        var stockSpec = new ExpressionSpecification<Product>(p => p.Stock > 10);
        var combinedSpec = colorSpec.And(stockSpec).ToExpression().Compile();

        // when
        var result = _products.Where(combinedSpec).ToList();

        // then
        result.Should().ContainSingle().And.ContainSingle(p => p.Name == "Red Mug");
    }

    [Fact]
    public void or_should_return_elements_satisfying_either_specification()
    {
        // given
        var colorSpec = new ExpressionSpecification<Product>(p => p.Color == Color.Red);
        var stockSpec = new ExpressionSpecification<Product>(p => p.Stock >= 10);
        var combinedSpec = colorSpec.Or(stockSpec).ToExpression().Compile();

        // when
        var result = _products.Where(combinedSpec).ToList();

        // then
        result
            .Should()
            .HaveCount(4)
            .And.Contain(p => p.Name == "Red Chair")
            .And.Contain(p => p.Name == "Red Mug")
            .And.Contain(p => p.Name == "Green Plate")
            .And.Contain(p => p.Name == "White Lamp");
    }

    [Fact]
    public void and_not_should_return_elements_satisfying_first_but_not_second_specification()
    {
        // given
        var colorSpec = new ExpressionSpecification<Product>(p => p.Color == Color.Green);
        var stockSpec = new ExpressionSpecification<Product>(p => p.Stock > 10);
        var combinedSpec = colorSpec.AndNot(stockSpec).ToExpression().Compile();

        // when
        var result = _products.Where(combinedSpec).ToList();

        // then
        result.Should().ContainSingle().And.ContainSingle(p => p.Name == "Green Table");
    }

    [Fact]
    public void not_should_return_elements_not_satisfying_specification()
    {
        // given
        var discontinuedSpec = new ExpressionSpecification<Product>(p => p.Available);
        var notSpec = discontinuedSpec.Not().ToExpression().Compile();

        // when
        var result = _products.Where(notSpec).ToList();

        // then
        result.Should().HaveCount(5).And.NotContain(p => p.Available);
    }
}
