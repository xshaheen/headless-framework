// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.NetTopologySuite.Constants;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Implementation;
using NetTopologySuite.IO.Converters;

namespace Headless.NetTopologySuite;

/// <summary>
/// Shared, preconfigured NetTopologySuite runtime services and factories (SRID 4326, HighPrecision,
/// NG overlay/relate). Kept separate from <see cref="GeoConstants"/> so that referencing a plain
/// constant does not trigger initialization of these heavier singletons.
/// </summary>
[PublicAPI]
public static class GeoServices
{
    public static readonly NtsGeometryServices NtsGeometryServices = CreateNtsGeometryServices();

    public static readonly GeometryFactory GeometryFactory = NtsGeometryServices.CreateGeometryFactory();

    public static NtsGeometryServices CreateNtsGeometryServices()
    {
        return new(
            CoordinateArraySequenceFactory.Instance,
            GeoConstants.HighPrecision,
            GeoConstants.GoogleMapsSrid,
            GeometryOverlay.NG,
            GeometryRelate.NG,
            new CoordinateEqualityComparer()
        );
    }

    public static GeoJsonConverterFactory CreateGeoJsonConverter()
    {
        return new GeoJsonConverterFactory(
            factory: GeometryFactory,
            writeGeometryBBox: false,
            idPropertyName: "id",
            ringOrientationOption: RingOrientationOption.EnforceRfc9746, // To match expected in SQL Server
            allowModifyingAttributesTables: false
        );
    }
}
