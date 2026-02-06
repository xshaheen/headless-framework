// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.NetTopologySuite.Constants;
using NetTopologySuite.Geometries;

namespace Tests.GeoExtensions;

public sealed class ConversionTests
{
    private static GeometryFactory Factory => GeoConstants.GeometryFactory;

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
    public void ToFeatureCollection_should_create_collection_with_features()
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
    public void ToFeatureCollection_should_create_empty_attributes()
    {
        var polygon = _CreateSquare();
        var geometries = new List<Geometry> { polygon };

        var collection = geometries.ToFeatureCollection();

        collection[0].Attributes.Should().NotBeNull();
        collection[0].Attributes.Count.Should().Be(0);
    }

    [Fact]
    public void ToFeatureCollection_should_handle_empty_list()
    {
        var geometries = new List<Geometry>();

        var collection = geometries.ToFeatureCollection();

        collection.Should().BeEmpty();
    }

    [Fact]
    public void AsMultiPolygon_should_wrap_polygon()
    {
        var polygon = _CreateSquare();

        var multi = polygon.AsMultiPolygon();

        ((object)multi).Should().BeOfType<MultiPolygon>();
        multi.NumGeometries.Should().Be(1);
        multi.GetGeometryN(0).Should().Be(polygon);
    }

    [Fact]
    public void AsMultiPolygon_should_return_multipolygon_unchanged()
    {
        var polygon = _CreateSquare();
        var multi = Factory.CreateMultiPolygon([polygon]);

        var result = multi.AsMultiPolygon();

        ((object)result).Should().BeSameAs(multi);
    }

    [Fact]
    public void AsMultiPolygon_extension_should_use_geometry_factory()
    {
        var polygon = _CreateSquare();

        var multi = polygon.AsMultiPolygon();

        multi.Factory.Should().Be(polygon.Factory);
    }

    [Fact]
    public void EnsurePolygonOrMulti_should_return_polygon()
    {
        var polygon = _CreateSquare();

        var result = polygon.EnsurePolygonOrMulti();

        result.Should().BeSameAs(polygon);
    }

    [Fact]
    public void EnsurePolygonOrMulti_should_return_multipolygon()
    {
        var polygon = _CreateSquare();
        var multi = Factory.CreateMultiPolygon([polygon]);

        var result = multi.EnsurePolygonOrMulti();

        ((object)result).Should().BeSameAs(multi);
    }

    [Fact]
    public void EnsurePolygonOrMulti_should_extract_single_polygon_from_collection()
    {
        var polygon = _CreateSquare();
        var collection = Factory.CreateGeometryCollection([polygon]);

        var result = collection.EnsurePolygonOrMulti();

        result.Should().Be(polygon);
    }

    [Fact]
    public void EnsurePolygonOrMulti_should_create_multipolygon_from_collection()
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
    public void EnsurePolygonOrMulti_should_throw_for_point()
    {
        var point = Factory.CreatePoint(new Coordinate(0, 0));

        var act = () => point.EnsurePolygonOrMulti();

        act.Should().Throw<InvalidOperationException>().WithMessage("*Point*");
    }

    [Fact]
    public void EnsurePolygonOrMulti_should_throw_for_linestring()
    {
        var line = Factory.CreateLineString([new Coordinate(0, 0), new Coordinate(1, 1)]);

        var act = () => line.EnsurePolygonOrMulti();

        act.Should().Throw<InvalidOperationException>().WithMessage("*LineString*");
    }
}
