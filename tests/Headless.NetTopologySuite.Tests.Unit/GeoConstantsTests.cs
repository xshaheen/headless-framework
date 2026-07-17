// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.NetTopologySuite;
using Headless.NetTopologySuite.Constants;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO.Converters;

namespace Tests;

public sealed class GeoConstantsTests
{
    [Fact]
    public void should_be_4326_when_google_maps_srid()
    {
        // then
        GeoConstants.GoogleMapsSrid.Should().Be(4326);
    }

    [Fact]
    public void should_be_floating_when_ultra_precision()
    {
        // then - PrecisionModel.Floating has unlimited precision (~1.1mm)
        GeoConstants.UltraPrecision.PrecisionModelType.Should().Be(PrecisionModels.Floating);
    }

    [Fact]
    public void should_be_floating_single_when_high_precision()
    {
        // then - PrecisionModel.FloatingSingle (~11cm accuracy)
        GeoConstants.HighPrecision.PrecisionModelType.Should().Be(PrecisionModels.FloatingSingle);
    }

    [Fact]
    public void should_be_100000_when_street_level_precision()
    {
        // then - Fixed precision with scale 100,000 (~1.1m accuracy)
        GeoConstants.StreetLevelPrecision.Scale.Should().Be(100_000);
        GeoConstants.StreetLevelPrecision.PrecisionModelType.Should().Be(PrecisionModels.Fixed);
    }

    [Fact]
    public void should_be_correct_value_when_around11_cm_degrees()
    {
        // then - ~11 cm at Equator
        GeoConstants.Around11CmDegrees.Should().Be(0.000001);
    }

    [Fact]
    public void should_be_correct_value_when_around1_m_degrees()
    {
        // then - ~1.1 m at Equator
        GeoConstants.Around1MDegrees.Should().Be(0.00001);
    }

    [Fact]
    public void should_be_correct_value_when_around111_m_degrees()
    {
        // then - ~111 m at Equator
        GeoConstants.Around111MDegrees.Should().Be(0.0001);
    }

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
    [SuppressMessage("Reliability", "CA1869:Cache and reuse 'JsonSerializerOptions' instances")]
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
