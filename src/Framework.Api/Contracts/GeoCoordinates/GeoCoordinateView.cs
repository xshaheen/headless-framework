// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Framework.Primitives;
using NetTopologySuite.Geometries;

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Framework.Api.Contracts;

public sealed record GeoCoordinateView(double Latitude, double Longitude)
{
    public override string ToString() => $"({Latitude}, {Longitude})";

    [return: NotNullIfNotNull(nameof(operand))]
    public static GeoCoordinateView? FromGeoCoordinate(GeoCoordinate? operand) => operand;

    [return: NotNullIfNotNull(nameof(operand))]
    public static GeoCoordinateView? FromCoordinate(Coordinate? operand) => operand;

    [return: NotNullIfNotNull(nameof(operand))]
    public static GeoCoordinateView? FromPoint(Point? operand) => operand;

    [return: NotNullIfNotNull(nameof(operand))]
    public static implicit operator GeoCoordinateView?(GeoCoordinate? operand)
    {
        return operand is null ? null : new(Latitude: operand.Latitude, Longitude: operand.Longitude);
    }

    [return: NotNullIfNotNull(nameof(operand))]
    public static implicit operator GeoCoordinateView?(Coordinate? operand)
    {
        return operand is null ? null : new(Latitude: operand.Y, Longitude: operand.X);
    }

    [return: NotNullIfNotNull(nameof(operand))]
    public static implicit operator GeoCoordinateView?(Point? operand)
    {
        return operand is null ? null : new(Latitude: operand.Y, Longitude: operand.X);
    }
}
