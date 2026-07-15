// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.NetTopologySuite;
using Headless.NetTopologySuite.Constants;
using NetTopologySuite.Algorithm;
using NetTopologySuite.Geometries;

namespace Tests.GeoExtensions;

public sealed class SimplifyTests
{
    private static GeometryFactory Factory => GeoServices.GeometryFactory;

    [Fact]
    public void should_reduce_vertex_count_when_simplify_geometry()
    {
        // given - polygon with many vertices (scaled to geographic coordinates)
        var complex = _CreateComplexPolygon();
        var originalCount = complex.Coordinates.Length;

        // when - simplify with larger tolerance to ensure reduction
        var simplified = ((Geometry)complex).Simplify(0.1);

        // then - should have fewer vertices
        simplified.Coordinates.Length.Should().BeLessThan(originalCount);
    }

    [Fact]
    public void should_preserve_topology_when_simplify_geometry()
    {
        // given
        var complex = _CreateComplexPolygon();

        // when
        var simplified = ((Geometry)complex).Simplify();

        // then - result should be valid geometry
        simplified.IsValid.Should().BeTrue();
    }

    [Fact]
    public void should_fix_invalid_result_when_simplify_geometry()
    {
        // given - create a polygon that when simplified might become invalid
        // Use larger tolerance to force aggressive simplification
        var complex = _CreateComplexPolygon();

        // when - simplify with larger tolerance
        var simplified = ((Geometry)complex).Simplify(GeoConstants.Around111MDegrees);

        // then - result should still be valid (auto-fixed if needed)
        simplified.IsValid.Should().BeTrue();
    }

    [Fact]
    public void should_return_empty_unchanged_when_simplify_polygon()
    {
        // given
        var empty = Factory.CreatePolygon();

        // when
        var result = empty.Simplify();

        // then
        result.Should().BeSameAs(empty);
    }

    [Fact]
    public void should_maintain_ccw_orientation_when_simplify_polygon()
    {
        // given - polygon with many vertices
        var complex = _CreateComplexPolygon();

        // when
        var simplified = complex.Simplify();

        // then - should maintain CCW orientation for shell
        if (!simplified.IsEmpty)
        {
            Orientation.IsCCW(simplified.Shell.CoordinateSequence).Should().BeTrue();
        }
    }

    [Fact]
    public void should_use_default_tolerance_when_simplify_polygon()
    {
        // given - polygon with many vertices
        var complex = _CreateComplexPolygon();

        // when - simplify without specifying tolerance (uses Around1MDegrees)
        var simplified1 = complex.Simplify();
        var simplified2 = complex.Simplify(GeoConstants.Around1MDegrees);

        // then - both should produce equivalent results
        simplified1.EqualsExact(simplified2).Should().BeTrue();
    }

    [Fact]
    public void should_simplify_all_polygons_when_simplify_multipolygon()
    {
        // given - multipolygon with two complex polygons
        var poly1 = _CreateComplexPolygon();
        var poly2 = _CreateOffsetComplexPolygon(5);
        var multi = Factory.CreateMultiPolygon([poly1, poly2]);
        var originalCounts = multi.Geometries.ConvertAll(g => g.Coordinates.Length);

        // when - use larger tolerance to ensure simplification
        var simplified = multi.Simplify(0.1);

        // then - each polygon should be simplified
        simplified.NumGeometries.Should().Be(2);
        for (var i = 0; i < simplified.NumGeometries; i++)
        {
            simplified.GetGeometryN(i).Coordinates.Length.Should().BeLessThan(originalCounts[i]);
        }
    }

    [Fact]
    public void should_return_empty_unchanged_when_simplify_multipolygon()
    {
        // given
        var empty = Factory.CreateMultiPolygon([]);

        // when
        var result = empty.Simplify();

        // then
        ((object)result)
            .Should()
            .BeSameAs(empty);
    }

    private static Polygon _CreateComplexPolygon()
    {
        // Create a polygon with many vertices (circle approximation)
        var coords = new List<Coordinate>();
        for (var i = 0; i < 36; i++)
        {
            var angle = i * 10 * Math.PI / 180;
            coords.Add(new Coordinate(Math.Cos(angle), Math.Sin(angle)));
        }
        // Close the ring
        coords.Add(coords[0].Copy());
        return Factory.CreatePolygon([.. coords]);
    }

    private static Polygon _CreateOffsetComplexPolygon(double offset)
    {
        // Create a polygon with many vertices offset from origin
        var coords = new List<Coordinate>();
        for (var i = 0; i < 36; i++)
        {
            var angle = i * 10 * Math.PI / 180;
            coords.Add(new Coordinate(offset + Math.Cos(angle), offset + Math.Sin(angle)));
        }
        // Close the ring
        coords.Add(coords[0].Copy());
        return Factory.CreatePolygon([.. coords]);
    }
}
