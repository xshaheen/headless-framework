// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.NetTopologySuite.Constants;
using NetTopologySuite.Algorithm;
using NetTopologySuite.Geometries;

namespace Tests.GeoExtensions;

public sealed class OrientationTests
{
    private static GeometryFactory Factory => GeoConstants.GeometryFactory;

    #region EnsureIsOrientedCounterClockwise - Polygon

    [Fact]
    public void EnsureIsOrientedCounterClockwise_polygon_should_reverse_cw_shell()
    {
        // given - CW polygon (incorrect for SQL Server)
        var cwPolygon = _CreateCwSquare();
        Orientation.IsCCW(cwPolygon.Shell.CoordinateSequence).Should().BeFalse();

        // when
        var result = cwPolygon.EnsureIsOrientedCounterClockwise();

        // then - should be CCW
        Orientation.IsCCW(result.Shell.CoordinateSequence).Should().BeTrue();
    }

    [Fact]
    public void EnsureIsOrientedCounterClockwise_polygon_should_keep_ccw_shell()
    {
        // given - already CCW polygon
        var ccwPolygon = _CreateCcwSquare();
        Orientation.IsCCW(ccwPolygon.Shell.CoordinateSequence).Should().BeTrue();

        // when
        var result = ccwPolygon.EnsureIsOrientedCounterClockwise();

        // then - should remain CCW
        Orientation.IsCCW(result.Shell.CoordinateSequence).Should().BeTrue();
    }

    [Fact]
    public void EnsureIsOrientedCounterClockwise_polygon_should_reverse_ccw_holes()
    {
        // given - CCW shell with CCW hole (hole should be CW)
        var polygon = _CreatePolygonWithHole(shellCcw: true, holeCw: false);
        Orientation.IsCCW(polygon.Holes[0].CoordinateSequence).Should().BeTrue();

        // when
        var result = polygon.EnsureIsOrientedCounterClockwise();

        // then - hole should be CW
        Orientation.IsCCW(result.Holes[0].CoordinateSequence).Should().BeFalse();
    }

    [Fact]
    public void EnsureIsOrientedCounterClockwise_polygon_should_keep_cw_holes()
    {
        // given - CCW shell with CW hole (already correct)
        var polygon = _CreatePolygonWithHole(shellCcw: true, holeCw: true);
        Orientation.IsCCW(polygon.Holes[0].CoordinateSequence).Should().BeFalse();

        // when
        var result = polygon.EnsureIsOrientedCounterClockwise();

        // then - hole should remain CW
        Orientation.IsCCW(result.Holes[0].CoordinateSequence).Should().BeFalse();
    }

    #endregion

    #region EnsureIsOrientedCounterClockwise - MultiPolygon

    [Fact]
    public void EnsureIsOrientedCounterClockwise_multipolygon_should_orient_all()
    {
        // given - multipolygon with mixed orientations
        var cw1 = _CreateCwSquare();
        var cw2 = __CreateCwSquareAt(5, 5);
        var multi = Factory.CreateMultiPolygon([cw1, cw2]);

        // when
        var result = multi.EnsureIsOrientedCounterClockwise();

        // then - all polygons should be CCW
        foreach (var polygon in result.Geometries.OfType<Polygon>())
        {
            Orientation.IsCCW(polygon.Shell.CoordinateSequence).Should().BeTrue();
        }
    }

    [Fact]
    public void EnsureIsOrientedCounterClockwise_multipolygon_should_return_empty_unchanged()
    {
        // given
        var empty = Factory.CreateMultiPolygon([]);

        // when
        var result = empty.EnsureIsOrientedCounterClockwise();

        // then
        ((object)result).Should().BeSameAs(empty);
    }

    #endregion

    #region EnsureIsOrientedCounterClockwise - Geometry dispatch

    [Fact]
    public void EnsureIsOrientedCounterClockwise_geometry_should_handle_polygon()
    {
        // given
        Geometry cwPolygon = _CreateCwSquare();

        // when
        var result = cwPolygon.EnsureIsOrientedCounterClockwise();

        // then
        var polygon = result.Should().BeOfType<Polygon>().Which;
        Orientation.IsCCW(polygon.Shell.CoordinateSequence).Should().BeTrue();
    }

    [Fact]
    public void EnsureIsOrientedCounterClockwise_geometry_should_handle_multipolygon()
    {
        // given
        Geometry multi = Factory.CreateMultiPolygon([_CreateCwSquare()]);

        // when
        var result = multi.EnsureIsOrientedCounterClockwise();

        // then
        var multiPolygon = result.Should().BeOfType<MultiPolygon>().Which;
        var polygon = (Polygon)multiPolygon.GetGeometryN(0);
        Orientation.IsCCW(polygon.Shell.CoordinateSequence).Should().BeTrue();
    }

    [Fact]
    public void EnsureIsOrientedCounterClockwise_geometry_should_handle_collection()
    {
        // given - geometry collection with polygon and multipolygon
        var polygon = _CreateCwSquare();
        var multiPolygon = Factory.CreateMultiPolygon([__CreateCwSquareAt(5, 5)]);
        Geometry collection = Factory.CreateGeometryCollection([polygon, multiPolygon]);

        // when
        var result = collection.EnsureIsOrientedCounterClockwise();

        // then - all nested polygons should be CCW
        var resultCollection = result.Should().BeOfType<GeometryCollection>().Which;
        var resultPolygon = resultCollection.GetGeometryN(0).Should().BeOfType<Polygon>().Which;
        Orientation.IsCCW(resultPolygon.Shell.CoordinateSequence).Should().BeTrue();

        var resultMulti = resultCollection.GetGeometryN(1).Should().BeOfType<MultiPolygon>().Which;
        var innerPolygon = (Polygon)resultMulti.GetGeometryN(0);
        Orientation.IsCCW(innerPolygon.Shell.CoordinateSequence).Should().BeTrue();
    }

    [Fact]
    public void EnsureIsOrientedCounterClockwise_geometry_should_return_point_unchanged()
    {
        // given
        Geometry point = Factory.CreatePoint(new Coordinate(1, 1));

        // when
        var result = point.EnsureIsOrientedCounterClockwise();

        // then
        result.Should().BeSameAs(point);
    }

    #endregion

    #region IsOrientedCounterClockwise

    [Fact]
    public void IsOrientedCounterClockwise_should_return_true_for_ccw_shell_cw_holes()
    {
        // given - correct orientation: CCW shell, CW hole
        var polygon = _CreatePolygonWithHole(shellCcw: true, holeCw: true);

        // when
        var result = polygon.IsOrientedCounterClockwise();

        // then
        result.Should().BeTrue();
    }

    [Fact]
    public void IsOrientedCounterClockwise_should_return_false_for_cw_shell()
    {
        // given - incorrect: CW shell
        var polygon = _CreateCwSquare();

        // when
        var result = polygon.IsOrientedCounterClockwise();

        // then
        result.Should().BeFalse();
    }

    [Fact]
    public void IsOrientedCounterClockwise_should_return_false_for_ccw_hole()
    {
        // given - incorrect: CCW hole (should be CW)
        var polygon = _CreatePolygonWithHole(shellCcw: true, holeCw: false);

        // when
        var result = polygon.IsOrientedCounterClockwise();

        // then
        result.Should().BeFalse();
    }

    #endregion

    #region Helpers

    // CCW square (correct for SQL Server shell)
    private static Polygon _CreateCcwSquare()
    {
        var coords = new[]
        {
            new Coordinate(0, 0),
            new Coordinate(1, 0),
            new Coordinate(1, 1),
            new Coordinate(0, 1),
            new Coordinate(0, 0),
        };
        return Factory.CreatePolygon(coords);
    }

    // CW square (needs reversal for shell)
    private static Polygon _CreateCwSquare()
    {
        var coords = new[]
        {
            new Coordinate(0, 0),
            new Coordinate(0, 1),
            new Coordinate(1, 1),
            new Coordinate(1, 0),
            new Coordinate(0, 0),
        };
        return Factory.CreatePolygon(coords);
    }

    // CW square at specific origin
    private static Polygon __CreateCwSquareAt(double x, double y)
    {
        var coords = new[]
        {
            new Coordinate(x, y),
            new Coordinate(x, y + 1),
            new Coordinate(x + 1, y + 1),
            new Coordinate(x + 1, y),
            new Coordinate(x, y),
        };
        return Factory.CreatePolygon(coords);
    }

    // Create polygon with hole
    private static Polygon _CreatePolygonWithHole(bool shellCcw, bool holeCw)
    {
        // Shell coords
        Coordinate[] shell = shellCcw
            ? [new(0, 0), new(10, 0), new(10, 10), new(0, 10), new(0, 0)]
            : [new(0, 0), new(0, 10), new(10, 10), new(10, 0), new(0, 0)];

        // Hole coords (inner ring)
        Coordinate[] hole = holeCw
            ? [new(2, 2), new(2, 8), new(8, 8), new(8, 2), new(2, 2)] // CW
            : [new(2, 2), new(8, 2), new(8, 8), new(2, 8), new(2, 2)]; // CCW

        return Factory.CreatePolygon(
            Factory.CreateLinearRing(shell),
            [Factory.CreateLinearRing(hole)]
        );
    }

    #endregion
}
