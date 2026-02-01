// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.NetTopologySuite.Constants;
using NetTopologySuite.Geometries;

namespace Tests.GeoExtensions;

public sealed class PermissiveOperationsTests
{
    private static GeometryFactory Factory => GeoConstants.GeometryFactory;

    private static Polygon _CreateSquare(double size = 1.0, double originX = 0, double originY = 0)
    {
        var coords = new[]
        {
            new Coordinate(originX, originY),
            new Coordinate(originX + size, originY),
            new Coordinate(originX + size, originY + size),
            new Coordinate(originX, originY + size),
            new Coordinate(originX, originY), // Close ring
        };
        return Factory.CreatePolygon(coords);
    }

    #region PermissiveOverlaps

    [Fact]
    public void PermissiveOverlaps_should_return_true_for_overlapping_polygons()
    {
        // given - two squares that partially overlap
        var square1 = _CreateSquare(originX: 0, originY: 0);
        var square2 = _CreateSquare(originX: 0.5, originY: 0.5);

        // when
        var result = square1.PermissiveOverlaps(square2);

        // then
        result.Should().BeTrue();
    }

    [Fact]
    public void PermissiveOverlaps_should_return_false_for_non_overlapping()
    {
        // given - two non-overlapping squares
        var square1 = _CreateSquare(originX: 0, originY: 0);
        var square2 = _CreateSquare(originX: 10, originY: 10);

        // when
        var result = square1.PermissiveOverlaps(square2);

        // then
        result.Should().BeFalse();
    }

    [Fact]
    public void PermissiveOverlaps_should_unwrap_single_geometry_collection()
    {
        // given - two squares wrapped in single-geometry collections
        var square1 = _CreateSquare(originX: 0, originY: 0);
        var square2 = _CreateSquare(originX: 0.5, originY: 0.5);
        var collection1 = Factory.CreateGeometryCollection([square1]);
        var collection2 = Factory.CreateGeometryCollection([square2]);

        // when
        var result = collection1.PermissiveOverlaps(collection2);

        // then
        result.Should().BeTrue();
    }

    [Fact]
    public void PermissiveOverlaps_should_reduce_precision_on_error()
    {
        // given - geometries that might cause topology exceptions
        // Use high-precision coordinates that may cause issues
        var coords1 = new[]
        {
            new Coordinate(0.00000001, 0.00000001),
            new Coordinate(1.00000001, 0.00000001),
            new Coordinate(1.00000001, 1.00000001),
            new Coordinate(0.00000001, 1.00000001),
            new Coordinate(0.00000001, 0.00000001),
        };
        var coords2 = new[]
        {
            new Coordinate(0.50000001, 0.50000001),
            new Coordinate(1.50000001, 0.50000001),
            new Coordinate(1.50000001, 1.50000001),
            new Coordinate(0.50000001, 1.50000001),
            new Coordinate(0.50000001, 0.50000001),
        };
        var polygon1 = Factory.CreatePolygon(coords1);
        var polygon2 = Factory.CreatePolygon(coords2);

        // when - this should not throw and should handle precision gracefully
        var act = () => polygon1.PermissiveOverlaps(polygon2);

        // then
        act.Should().NotThrow();
    }

    #endregion

    #region PermissiveIntersection

    [Fact]
    public void PermissiveIntersection_should_return_intersection()
    {
        // given - two overlapping squares
        var square1 = _CreateSquare(originX: 0, originY: 0);
        var square2 = _CreateSquare(originX: 0.5, originY: 0.5);

        // when
        var result = square1.PermissiveIntersection(square2);

        // then
        result.Should().NotBeNull();
        result.IsEmpty.Should().BeFalse();
        result.Area.Should().BeGreaterThan(0);
    }

    [Fact]
    public void PermissiveIntersection_should_unwrap_single_geometry_collection()
    {
        // given - two squares wrapped in single-geometry collections
        var square1 = _CreateSquare(originX: 0, originY: 0);
        var square2 = _CreateSquare(originX: 0.5, originY: 0.5);
        var collection1 = Factory.CreateGeometryCollection([square1]);
        var collection2 = Factory.CreateGeometryCollection([square2]);

        // when
        var result = collection1.PermissiveIntersection(collection2);

        // then
        result.Should().NotBeNull();
        result.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void PermissiveIntersection_should_reduce_precision_on_error()
    {
        // given - geometries with very high precision
        var coords1 = new[]
        {
            new Coordinate(0.00000001, 0.00000001),
            new Coordinate(1.00000001, 0.00000001),
            new Coordinate(1.00000001, 1.00000001),
            new Coordinate(0.00000001, 1.00000001),
            new Coordinate(0.00000001, 0.00000001),
        };
        var coords2 = new[]
        {
            new Coordinate(0.50000001, 0.50000001),
            new Coordinate(1.50000001, 0.50000001),
            new Coordinate(1.50000001, 1.50000001),
            new Coordinate(0.50000001, 1.50000001),
            new Coordinate(0.50000001, 0.50000001),
        };
        var polygon1 = Factory.CreatePolygon(coords1);
        var polygon2 = Factory.CreatePolygon(coords2);

        // when - this should not throw
        var act = () => polygon1.PermissiveIntersection(polygon2);

        // then
        act.Should().NotThrow();
    }

    #endregion

    #region PermissiveUnion

    [Fact]
    public void PermissiveUnion_should_return_union()
    {
        // given - two overlapping squares
        var square1 = _CreateSquare(originX: 0, originY: 0);
        var square2 = _CreateSquare(originX: 0.5, originY: 0.5);

        // when
        var result = square1.PermissiveUnion(square2);

        // then
        result.Should().NotBeNull();
        result.IsEmpty.Should().BeFalse();
        result.Area.Should().BeGreaterThan(square1.Area);
        result.Area.Should().BeLessThan(square1.Area + square2.Area);
    }

    [Fact]
    public void PermissiveUnion_should_unwrap_single_geometry_collection()
    {
        // given - two squares wrapped in single-geometry collections
        var square1 = _CreateSquare(originX: 0, originY: 0);
        var square2 = _CreateSquare(originX: 0.5, originY: 0.5);
        var collection1 = Factory.CreateGeometryCollection([square1]);
        var collection2 = Factory.CreateGeometryCollection([square2]);

        // when
        var result = collection1.PermissiveUnion(collection2);

        // then
        result.Should().NotBeNull();
        result.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void PermissiveUnion_should_reduce_precision_on_error()
    {
        // given - geometries with very high precision
        var coords1 = new[]
        {
            new Coordinate(0.00000001, 0.00000001),
            new Coordinate(1.00000001, 0.00000001),
            new Coordinate(1.00000001, 1.00000001),
            new Coordinate(0.00000001, 1.00000001),
            new Coordinate(0.00000001, 0.00000001),
        };
        var coords2 = new[]
        {
            new Coordinate(0.50000001, 0.50000001),
            new Coordinate(1.50000001, 0.50000001),
            new Coordinate(1.50000001, 1.50000001),
            new Coordinate(0.50000001, 1.50000001),
            new Coordinate(0.50000001, 0.50000001),
        };
        var polygon1 = Factory.CreatePolygon(coords1);
        var polygon2 = Factory.CreatePolygon(coords2);

        // when - this should not throw
        var act = () => polygon1.PermissiveUnion(polygon2);

        // then
        act.Should().NotThrow();
    }

    #endregion

    #region PermissiveDifference

    [Fact]
    public void PermissiveDifference_should_return_difference()
    {
        // given - two overlapping squares
        var square1 = _CreateSquare(originX: 0, originY: 0);
        var square2 = _CreateSquare(originX: 0.5, originY: 0.5);

        // when
        var result = square1.PermissiveDifference(square2);

        // then
        result.Should().NotBeNull();
        result.IsEmpty.Should().BeFalse();
        result.Area.Should().BeLessThan(square1.Area);
        result.Area.Should().BeGreaterThan(0);
    }

    [Fact]
    public void PermissiveDifference_should_unwrap_single_geometry_collection()
    {
        // given - two squares wrapped in single-geometry collections
        var square1 = _CreateSquare(originX: 0, originY: 0);
        var square2 = _CreateSquare(originX: 0.5, originY: 0.5);
        var collection1 = Factory.CreateGeometryCollection([square1]);
        var collection2 = Factory.CreateGeometryCollection([square2]);

        // when
        var result = collection1.PermissiveDifference(collection2);

        // then
        result.Should().NotBeNull();
        result.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void PermissiveDifference_should_reduce_precision_on_error()
    {
        // given - geometries with very high precision
        var coords1 = new[]
        {
            new Coordinate(0.00000001, 0.00000001),
            new Coordinate(1.00000001, 0.00000001),
            new Coordinate(1.00000001, 1.00000001),
            new Coordinate(0.00000001, 1.00000001),
            new Coordinate(0.00000001, 0.00000001),
        };
        var coords2 = new[]
        {
            new Coordinate(0.50000001, 0.50000001),
            new Coordinate(1.50000001, 0.50000001),
            new Coordinate(1.50000001, 1.50000001),
            new Coordinate(0.50000001, 1.50000001),
            new Coordinate(0.50000001, 0.50000001),
        };
        var polygon1 = Factory.CreatePolygon(coords1);
        var polygon2 = Factory.CreatePolygon(coords2);

        // when - this should not throw
        var act = () => polygon1.PermissiveDifference(polygon2);

        // then
        act.Should().NotThrow();
    }

    #endregion

    #region ComputeOverlap

    [Fact]
    public void ComputeOverlap_should_return_intersection_when_overlapping()
    {
        // given - two overlapping squares
        var square1 = _CreateSquare(originX: 0, originY: 0);
        var square2 = _CreateSquare(originX: 0.5, originY: 0.5);

        // when
        var result = square1.ComputeOverlap(square2);

        // then
        result.Should().NotBeNull();
        result!.IsEmpty.Should().BeFalse();
        result.Area.Should().BeGreaterThan(0);
    }

    [Fact]
    public void ComputeOverlap_should_return_null_when_not_overlapping()
    {
        // given - two non-overlapping squares
        var square1 = _CreateSquare(originX: 0, originY: 0);
        var square2 = _CreateSquare(originX: 10, originY: 10);

        // when
        var result = square1.ComputeOverlap(square2);

        // then
        result.Should().BeNull();
    }

    #endregion
}
