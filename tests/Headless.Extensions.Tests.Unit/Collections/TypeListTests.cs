// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Collections;

namespace Tests.Collections;

public sealed class TypeListTests
{
    private interface IShape;

    private sealed class Rectangle : IShape;

    private sealed class Circle : IShape;

    private readonly TypeList<IShape> _shapeTypeList = [];

    [Fact]
    public void should_add_shape_to_list_when_add()
    {
        // when
        _shapeTypeList.Add<Rectangle>();

        // then
        _shapeTypeList.Should().ContainSingle();
        _shapeTypeList.Contains<Rectangle>().Should().BeTrue();
    }

    [Fact]
    public void should_prevent_duplicate_shapes_when_try_add()
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
    public void should_throw_exception_for_invalid_type_when_add()
    {
        // when
        var act = () => _shapeTypeList.Add(typeof(string));

        // then
        act.Should().Throw<ArgumentException>().WithMessage("*should be instance of*IShape*");
    }

    [Fact]
    public void should_return_true_for_registered_shape_when_contains()
    {
        // given
        _shapeTypeList.Add<Circle>();

        // when
        var containsCircle = _shapeTypeList.Contains<Circle>();

        // then
        containsCircle.Should().BeTrue();
    }

    [Fact]
    public void should_remove_shape_from_list_when_remove()
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
