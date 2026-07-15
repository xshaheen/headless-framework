// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.NetTopologySuite;
using NetTopologySuite.Geometries;

namespace Tests.GeoExtensions;

public sealed class ConversionTests
{
    private static GeometryFactory Factory => GeoServices.GeometryFactory;

    private static Polygon _CreateSquare()
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

    [Fact]
    public void should_create_collection_with_features_when_to_feature_collection()
    {
        var polygon1 = _CreateSquare();
        var polygon2 = _CreateSquare();
        var geometries = new List<Geometry> { polygon1, polygon2 };

        var collection = geometries.ToFeatureCollection();

        collection.Should().HaveCount(2);
        collection[0].Geometry.Should().Be(polygon1);
        collection[1].Geometry.Should().Be(polygon2);
    }

    [Fact]
    public void should_create_empty_attributes_when_to_feature_collection()
    {
        var polygon = _CreateSquare();
        var geometries = new List<Geometry> { polygon };

        var collection = geometries.ToFeatureCollection();

        collection[0].Attributes.Should().NotBeNull();
        collection[0].Attributes.Count.Should().Be(0);
    }

    [Fact]
    public void should_handle_empty_list_when_to_feature_collection()
    {
        var geometries = new List<Geometry>();

        var collection = geometries.ToFeatureCollection();

        collection.Should().BeEmpty();
    }

    [Fact]
    public void should_wrap_polygon_when_as_multi_polygon()
    {
        var polygon = _CreateSquare();

        var multi = polygon.AsMultiPolygon();

        ((object)multi).Should().BeOfType<MultiPolygon>();
        multi.NumGeometries.Should().Be(1);
        multi.GetGeometryN(0).Should().Be(polygon);
    }

    [Fact]
    public void should_return_multipolygon_unchanged_when_as_multi_polygon()
    {
        var polygon = _CreateSquare();
        var multi = Factory.CreateMultiPolygon([polygon]);

        var result = multi.AsMultiPolygon();

        ((object)result).Should().BeSameAs(multi);
    }

    [Fact]
    public void should_use_geometry_factory_when_as_multi_polygon_extension()
    {
        var polygon = _CreateSquare();

        var multi = polygon.AsMultiPolygon();

        multi.Factory.Should().Be(polygon.Factory);
    }

    [Fact]
    public void should_return_polygon_when_ensure_polygon_or_multi()
    {
        var polygon = _CreateSquare();

        var result = polygon.EnsurePolygonOrMulti();

        result.Should().BeSameAs(polygon);
    }

    [Fact]
    public void should_return_multipolygon_when_ensure_polygon_or_multi()
    {
        var polygon = _CreateSquare();
        var multi = Factory.CreateMultiPolygon([polygon]);

        var result = multi.EnsurePolygonOrMulti();

        ((object)result).Should().BeSameAs(multi);
    }

    [Fact]
    public void should_extract_single_polygon_from_collection_when_ensure_polygon_or_multi()
    {
        var polygon = _CreateSquare();
        var collection = Factory.CreateGeometryCollection([polygon]);

        var result = collection.EnsurePolygonOrMulti();

        result.Should().Be(polygon);
    }

    [Fact]
    public void should_create_multipolygon_from_collection_when_ensure_polygon_or_multi()
    {
        var polygon1 = _CreateSquare();
        var polygon2 = _CreateSquare();
        var collection = Factory.CreateGeometryCollection([polygon1, polygon2]);

        var result = collection.EnsurePolygonOrMulti();

        result.Should().BeOfType<MultiPolygon>();
        var multi = (MultiPolygon)result;
        multi.NumGeometries.Should().Be(2);
    }

    [Fact]
    public void should_extract_polygons_nested_in_sub_collections_when_ensure_polygon_or_multi()
    {
        // A polygon nested inside an inner GeometryCollection must not be silently dropped.
        var polygon1 = _CreateSquare();
        var polygon2 = _CreateSquare();
        var inner = Factory.CreateGeometryCollection([polygon2]);
        var outer = Factory.CreateGeometryCollection([polygon1, inner]);

        var result = outer.EnsurePolygonOrMulti();

        result.Should().BeOfType<MultiPolygon>();
        ((MultiPolygon)result).NumGeometries.Should().Be(2);
    }

    [Fact]
    public void should_throw_for_point_when_ensure_polygon_or_multi()
    {
        var point = Factory.CreatePoint(new Coordinate(0, 0));

        var act = () => point.EnsurePolygonOrMulti();

        act.Should().Throw<InvalidOperationException>().WithMessage("*Point*");
    }

    [Fact]
    public void should_throw_for_linestring_when_ensure_polygon_or_multi()
    {
        var line = Factory.CreateLineString([new Coordinate(0, 0), new Coordinate(1, 1)]);

        var act = () => line.EnsurePolygonOrMulti();

        act.Should().Throw<InvalidOperationException>().WithMessage("*LineString*");
    }
}
