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
    /// <summary>
    /// Shared <see cref="global::NetTopologySuite.NtsGeometryServices"/> singleton configured for SRID 4326,
    /// <see cref="GeoConstants.HighPrecision"/>, and the next-generation (NG) overlay and relate engines.
    /// </summary>
    public static readonly NtsGeometryServices NtsGeometryServices = CreateNtsGeometryServices();

    /// <summary>
    /// Shared <see cref="global::NetTopologySuite.Geometries.GeometryFactory"/> created from
    /// <see cref="NtsGeometryServices"/>; use it to build geometries that inherit the framework's standard SRID and
    /// precision model.
    /// </summary>
    public static readonly GeometryFactory GeometryFactory = NtsGeometryServices.CreateGeometryFactory();

    /// <summary>
    /// Creates a fresh <see cref="global::NetTopologySuite.NtsGeometryServices"/> with the framework's standard
    /// configuration (coordinate-array sequences, <see cref="GeoConstants.HighPrecision"/>, SRID 4326, NG
    /// overlay/relate). Prefer the cached <see cref="NtsGeometryServices"/> field unless an isolated instance is required.
    /// </summary>
    /// <returns>A new, fully configured <see cref="global::NetTopologySuite.NtsGeometryServices"/>.</returns>
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

    /// <summary>
    /// Creates a <see cref="GeoJsonConverterFactory"/> bound to <see cref="GeometryFactory"/> for
    /// <c>System.Text.Json</c> GeoJSON (de)serialization. Configured to omit bounding boxes, use <c>"id"</c> as the
    /// id property, enforce RFC&#160;9746 ring orientation (to match SQL Server), and treat attribute tables as read-only.
    /// </summary>
    /// <returns>A configured <see cref="GeoJsonConverterFactory"/>.</returns>
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
