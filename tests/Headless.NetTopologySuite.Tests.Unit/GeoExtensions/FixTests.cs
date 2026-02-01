// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.NetTopologySuite.Constants;
using NetTopologySuite.Geometries;

namespace Tests.GeoExtensions;

public sealed class FixTests
{
    private static GeometryFactory Factory => GeoConstants.GeometryFactory;

    [Fact]
    public void Fix_should_return_valid_geometry_unchanged()
    {
        // given - valid square polygon
        var coords = new Coordinate[]
        {
            new(0, 0),
            new(1, 0),
            new(1, 1),
            new(0, 1),
            new(0, 0),
        };
        var polygon = Factory.CreatePolygon(coords);

        // when
        var result = polygon.Fix();

        // then - valid geometry returned as-is
        result.Should().BeSameAs(polygon);
    }

    [Fact]
    public void Fix_should_return_empty_geometry_unchanged()
    {
        // given
        var empty = Factory.CreatePolygon();

        // when
        var result = empty.Fix();

        // then
        result.Should().BeSameAs(empty);
    }

    [Fact]
    public void Fix_should_fix_self_intersecting_polygon_with_buffer()
    {
        // given - figure-8 self-intersecting polygon
        var polygon = _CreateSelfIntersecting();
        polygon.IsValid.Should().BeFalse();

        // when
        var result = polygon.Fix();

        // then - result should be valid
        result.IsValid.Should().BeTrue();
        result.Should().NotBeSameAs(polygon);
    }

    [Fact]
    public void Fix_should_use_GeometryFixer_when_buffer_fails()
    {
        // given - create a polygon with a spike (degenerate geometry that Buffer(0) may not fix)
        // This polygon has collinear points and a spike that makes it invalid
        var coords = new Coordinate[]
        {
            new(0, 0),
            new(2, 0),
            new(2, 2),
            new(1, 2),
            new(1, 3), // spike out
            new(1, 2), // spike back (duplicate point creates issue)
            new(0, 2),
            new(0, 0),
        };
        var polygon = Factory.CreatePolygon(coords);

        // when
        var result = polygon.Fix();

        // then - should still produce valid geometry
        result.IsValid.Should().BeTrue();
    }

    private static Polygon _CreateSelfIntersecting()
    {
        // Figure-8 (self-intersecting)
        var coords = new Coordinate[]
        {
            new(0, 0),
            new(1, 1),
            new(1, 0),
            new(0, 1),
            new(0, 0),
        };
        return Factory.CreatePolygon(coords);
    }
}
