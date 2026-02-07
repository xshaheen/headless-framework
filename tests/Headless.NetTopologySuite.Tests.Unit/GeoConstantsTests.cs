// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.NetTopologySuite.Constants;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO.Converters;

namespace Tests;

public sealed class GeoConstantsTests
{
    [Fact]
    public void GoogleMapsSrid_should_be_4326()
    {
        // then
        GeoConstants.GoogleMapsSrid.Should().Be(4326);
    }

    [Fact]
    public void UltraPrecision_should_be_floating()
    {
        // then - PrecisionModel.Floating has unlimited precision (~1.1mm)
        GeoConstants.UltraPrecision.PrecisionModelType.Should().Be(PrecisionModels.Floating);
    }

    [Fact]
    public void HighPrecision_should_be_floating_single()
    {
        // then - PrecisionModel.FloatingSingle (~11cm accuracy)
        GeoConstants.HighPrecision.PrecisionModelType.Should().Be(PrecisionModels.FloatingSingle);
    }

    [Fact]
    public void StreetLevelPrecision_should_be_100000()
    {
        // then - Fixed precision with scale 100,000 (~1.1m accuracy)
        GeoConstants.StreetLevelPrecision.Scale.Should().Be(100_000);
        GeoConstants.StreetLevelPrecision.PrecisionModelType.Should().Be(PrecisionModels.Fixed);
    }

    [Fact]
    public void Around11CmDegrees_should_be_correct_value()
    {
        // then - ~11 cm at Equator
        GeoConstants.Around11CmDegrees.Should().Be(0.000001);
    }

    [Fact]
    public void Around1MDegrees_should_be_correct_value()
    {
        // then - ~1.1 m at Equator
        GeoConstants.Around1MDegrees.Should().Be(0.00001);
    }

    [Fact]
    public void Around111MDegrees_should_be_correct_value()
    {
        // then - ~111 m at Equator
        GeoConstants.Around111MDegrees.Should().Be(0.0001);
    }

    [Fact]
    public void NtsGeometryServices_should_be_configured_with_correct_srid()
    {
        // then
        GeoConstants.NtsGeometryServices.DefaultSRID.Should().Be(4326);
    }

    [Fact]
    public void NtsGeometryServices_should_use_high_precision()
    {
        // then
        GeoConstants.NtsGeometryServices.DefaultPrecisionModel.Should().BeSameAs(GeoConstants.HighPrecision);
    }

    [Fact]
    public void GeometryFactory_should_have_correct_srid()
    {
        // then
        GeoConstants.GeometryFactory.SRID.Should().Be(4326);
    }

    [Fact]
    public void CreateNtsGeometryServices_should_return_new_instance()
    {
        // when
        var services1 = GeoConstants.CreateNtsGeometryServices();
        var services2 = GeoConstants.CreateNtsGeometryServices();

        // then - Each call creates a new instance (not singleton)
        services1.Should().NotBeSameAs(services2);
        services1.DefaultSRID.Should().Be(4326);
    }

    [Fact]
    public void CreateGeoJsonConverter_should_return_converter_with_correct_settings()
    {
        // when
        var converter = GeoConstants.CreateGeoJsonConverter();

        // then - RingOrientationOption.EnforceRfc9746 should be set
        converter.Should().NotBeNull();
        converter.Should().BeOfType<GeoJsonConverterFactory>();
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA1869:Cache and reuse 'JsonSerializerOptions' instances"
    )]
    public void CreateGeoJsonConverter_should_not_write_bbox()
    {
        // given
        var converter = GeoConstants.CreateGeoJsonConverter();
        var options = new System.Text.Json.JsonSerializerOptions { Converters = { converter } };

        // when - Create a simple point and serialize it
        var point = GeoConstants.GeometryFactory.CreatePoint(new Coordinate(10, 20));
        var json = System.Text.Json.JsonSerializer.Serialize(point, options);

        // then - JSON should not contain bbox
        json.Should().NotContain("bbox");
    }
}
