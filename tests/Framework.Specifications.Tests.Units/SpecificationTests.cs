// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Linq.Expressions;
using Framework.Specifications;
using Tests.Models;

namespace Tests;

public sealed class SpecificationTests
{
    # region dumy data

    private readonly List<Product> _products =
    [
        new() { Name = "Red Chair", Price = 49.99m, Category = "Furniture", Stock = 5, Color = Color.Red, Available = true },
        new() { Name = "Green Table", Price = 18.99m, Category = "Furniture", Stock = 9, Color = Color.Default, Available = false },
        new() { Name = "Black Sofa", Price = 299.99m, Category = "Furniture", Stock = 0, Color = Color.Black, Available = false },
        new() { Name = "White Lamp", Price = 19.99m, Category = "Lighting", Stock = 0, Color = Color.White, Available = false },
        new() { Name = "Red Mug", Price = 9.99m, Category = "Kitchenware", Stock = 15, Color = Color.Red, Available = true },
        new() { Name = "Green Plate", Price = 4.99m, Category = "Kitchenware", Stock = 20, Color = Color.Green, Available = true },
    ];

    #endregion

    #region AndNotSpecification

    [Fact]
    public void and_not_specification_should_return_only_elements_satisfying_left_and_not_right_specifications()
    {
        // given
        var priceSpec = new PriceGreaterThanSpecification(50);
        var colorSpec = new ColorSpecification(Color.Black);

        var andNotSpec = new AndSpecification<Product>(priceSpec, colorSpec).ToExpression().Compile();

        // when
        var result = _products
            .Where(p => andNotSpec(p))
            .ToList();

        // then
        result.Should().ContainSingle();
    }

    #endregion

    #region AndSpecification

    [Fact]
    public void and__specification_should_return_only_elements_satisfying_left_and__right_specifications()
    {
        // given
        var priceSpec = new PriceGreaterThanSpecification(50);
        var discontinuedSpec = new IsAvailableSpecSpecification();

        var andNotSpec = new AndNotSpecification<Product>(priceSpec, discontinuedSpec).ToExpression().Compile();

        // when
        var result = _products
            .Where(p => andNotSpec(p))
            .ToList();

        // then
        result.Should().ContainSingle();
    }

    #endregion

    #region NoneSpecification

    [Fact]
    public void none_specification_should_return_false_for_all_elements()
    {
        // given
        var noneSpec = new NoneSpecification<Product>();

        // when
        var result = _products.Where(p => noneSpec.ToExpression().Compile()(p)).ToList();

        // then
        result.Should().BeEmpty();
    }

    #endregion

    #region NotSpecification

    [Fact]
    public void not_specification_should_return_true_for_elements_that_do_not_match_original_specification()
    {
        // given
        var priceGreaterThanSpec = new PriceGreaterThanSpecification(50);
        var notSpec = new NotSpecification<Product>(priceGreaterThanSpec);

        // when
        var result = _products.Where(p => notSpec.ToExpression().Compile()(p)).ToList();

        // then
        result.Should().Contain(p => p.Price < 50);
        result.Should().HaveCount(5);
    }

    #endregion

    #region orSpecification

    [Fact]
    public void or__specification_should_return_elements_satisfying_left_or__right_specifications()
    {
        // given
        var stockSpec = new InStockSpecification();
        var availableSpec = new IsAvailableSpecSpecification();
        var orSpec = new OrSpecification<Product>(stockSpec, availableSpec).ToExpression().Compile();

        // when
        var result = _products
            .Where(p => orSpec(p))
            .ToList();

        // then
        result.Should().HaveCount(4);
    }

    #endregion

    #region Expression Specification
    [Fact]
    public void expression_specification_should_filter_correctly_when_applied()
    {
        // given
        var specification = new ExpressionSpecification<Product>(p => p.Color == Color.Red).ToExpression().Compile();

        // when
        var result = _products.Where(specification).ToList();

        // then
        result.Should().HaveCount(2)
            .And.OnlyContain(p => p.Color == Color.Red);
    }
    #endregion

    #region Specfications helper Classes

    private class PriceGreaterThanSpecification(decimal price) : Specification<Product>
    {
        public override Expression<Func<Product, bool>> ToExpression()
        {
            return p => p.Price > price;
        }
    }

    private class IsAvailableSpecSpecification : Specification<Product>
    {
        public override Expression<Func<Product, bool>> ToExpression()
        {
            return p => p.Available;
        }
    }

    private class ColorSpecification(Color color) : Specification<Product>
    {
        public override Expression<Func<Product, bool>> ToExpression()
        {
            return p => p.Color == color;
        }
    }
    private class InStockSpecification : Specification<Product>
    {
        public override Expression<Func<Product, bool>> ToExpression()
        {
            return p => p.Stock > 0;
        }
    }
    #endregion
}
