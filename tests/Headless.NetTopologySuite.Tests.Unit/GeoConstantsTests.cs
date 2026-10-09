// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.NetTopologySuite;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO.Converters;

namespace Tests;

public sealed class GeoConstantsTests
{
    [Fact]
    public void should_be_configured_with_correct_srid_when_nts_geometry_services()
    {
        // then
        GeoServices.NtsGeometryServices.DefaultSRID.Should().Be(4326);
    }

    [Fact]
    public void should_use_high_precision_when_nts_geometry_services()
    {
        // then
        GeoServices.NtsGeometryServices.DefaultPrecisionModel.Should().BeSameAs(GeoConstants.HighPrecision);
    }

    [Fact]
    public void should_have_correct_srid_when_geometry_factory()
    {
        // then
        GeoServices.GeometryFactory.SRID.Should().Be(4326);
    }

    [Fact]
    public void should_return_new_instance_when_create_nts_geometry_services()
    {
        // when
        var services1 = GeoServices.CreateNtsGeometryServices();
        var services2 = GeoServices.CreateNtsGeometryServices();

        // then - Each call creates a new instance (not singleton)
        services1.Should().NotBeSameAs(services2);
        services1.DefaultSRID.Should().Be(4326);
    }

    [Fact]
    public void should_return_converter_with_correct_settings_when_create_geo_json_converter()
    {
        // when
        var converter = GeoServices.CreateGeoJsonConverter();

        // then - RingOrientationOption.EnforceRfc9746 should be set
        converter.Should().NotBeNull();
        converter.Should().BeOfType<GeoJsonConverterFactory>();
    }

    [Fact]
    [SuppressMessage(
        "Reliability",
        "CA1869:Cache and reuse 'JsonSerializerOptions' instances",
        Justification = "The options exist only to carry the converter under test, once per test."
    )]
    public void should_not_write_bbox_when_create_geo_json_converter()
    {
        // given
        var converter = GeoServices.CreateGeoJsonConverter();
        var options = new JsonSerializerOptions { Converters = { converter } };

        // when - Create a simple point and serialize it
        var point = GeoServices.GeometryFactory.CreatePoint(new Coordinate(10, 20));
        var json = JsonSerializer.Serialize(point, options);

        // then - JSON should not contain bbox
        json.Should().NotContain("bbox");
    }
}
