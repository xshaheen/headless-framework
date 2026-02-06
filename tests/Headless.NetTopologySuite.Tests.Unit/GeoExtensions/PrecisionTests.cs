// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.NetTopologySuite.Constants;
using NetTopologySuite.Geometries;

namespace Tests.GeoExtensions;

public sealed class PrecisionTests
{
    private static GeometryFactory Factory => GeoConstants.GeometryFactory;

    [Fact]
    public void ChangePrecision_geometry_should_reduce_precision()
    {
        // High precision point with many decimal places
        var point = Factory.CreatePoint(30.123456789, 31.987654321);

        var reduced = (Point)point.ChangePrecision(GeoConstants.StreetLevelPrecision);

        // Street level precision (1e5) should reduce decimal places
        reduced.X.Should().Be(30.12346); // Rounded to 5 decimal places
        reduced.Y.Should().Be(31.98765);
    }

    [Fact]
    public void ChangePrecision_geometry_should_return_same_when_precision_matches()
    {
        var point = Factory.CreatePoint(30.0, 31.0);

        var result = point.ChangePrecision(GeoConstants.HighPrecision);

        result.Should().BeSameAs(point);
    }

    [Fact]
    public void ChangePrecision_factory_should_reduce_precision()
    {
        var point = Factory.CreatePoint(30.123456789, 31.987654321);
        var streetLevelFactory = new GeometryFactory(GeoConstants.StreetLevelPrecision, GeoConstants.GoogleMapsSrid);

        var reduced = streetLevelFactory.ChangePrecision(point);

        reduced.X.Should().Be(30.12346);
        reduced.Y.Should().Be(31.98765);
    }

    [Fact]
    public void ChangePrecision_factory_should_return_same_when_precision_matches()
    {
        var point = Factory.CreatePoint(30.0, 31.0);

        var result = Factory.ChangePrecision(point);

        result.Should().BeSameAs(point);
    }

    [Fact]
    public void ChangePrecision_factory_should_convert_multipolygon_to_polygon_and_back()
    {
        // Create a simple square polygon
        var coordinates = new Coordinate[]
        {
            new(0.123456789, 0.123456789),
            new(1.123456789, 0.123456789),
            new(1.123456789, 1.123456789),
            new(0.123456789, 1.123456789),
            new(0.123456789, 0.123456789), // Close the ring
        };
        var polygon = Factory.CreatePolygon(coordinates);
        var multiPolygon = Factory.CreateMultiPolygon([polygon]);

        var streetLevelFactory = new GeometryFactory(GeoConstants.StreetLevelPrecision, GeoConstants.GoogleMapsSrid);

        var result = streetLevelFactory.ChangePrecision(multiPolygon);

        // Should preserve MultiPolygon type even if precision reduction collapsed to single polygon
        ((object)result)
            .Should()
            .BeAssignableTo<MultiPolygon>();
    }
}
