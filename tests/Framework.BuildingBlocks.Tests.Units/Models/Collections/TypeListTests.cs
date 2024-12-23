// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Collections;

namespace Tests.Models.Collections;

public sealed class TypeListTests
{
    private interface IShape;

    private sealed class Rectangle : IShape;

    private sealed class Circle : IShape;

    private readonly TypeList<IShape> _shapeTypeList = [];

    [Fact]
    public void add_should_add_shape_to_list()
    {
        // when
        _shapeTypeList.Add<Rectangle>();

        // then
        _shapeTypeList.Should().ContainSingle();
        _shapeTypeList.Contains<Rectangle>().Should().BeTrue();
    }

    [Fact]
    public void tryAdd_should_prevent_duplicate_shapes()
    {
        // when
        var firstAdd = _shapeTypeList.TryAdd<Rectangle>();
        var secondAdd = _shapeTypeList.TryAdd<Rectangle>();

        // then
        firstAdd.Should().BeTrue();
        secondAdd.Should().BeFalse();
        _shapeTypeList.Should().ContainSingle();
    }

    [Fact]
    public void add_should_throw_exception_for_invalid_type()
    {
        // when
        Action act = () => _shapeTypeList.Add(typeof(string));

        // then
        act.Should().Throw<ArgumentException>().WithMessage("*should be instance of*IShape*");
    }

    [Fact]
    public void contains_should_return_true_for_registered_shape()
    {
        // given
        _shapeTypeList.Add<Circle>();

        // when
        var containsCircle = _shapeTypeList.Contains<Circle>();

        // then
        containsCircle.Should().BeTrue();
    }

    [Fact]
    public void remove_should_remove_shape_from_list()
    {
        // given
        _shapeTypeList.Add<Rectangle>();

        // when
        _shapeTypeList.Remove<Rectangle>();

        // then
        _shapeTypeList.Contains<Rectangle>().Should().BeFalse();
        _shapeTypeList.Should().BeEmpty();
    }
}
