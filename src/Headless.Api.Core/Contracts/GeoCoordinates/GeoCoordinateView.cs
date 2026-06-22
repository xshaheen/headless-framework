// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Api.Contracts;

/// <summary>
/// API response view for a geographic coordinate pair. Maps the domain
/// <see cref="GeoCoordinate"/> primitive to a serializable record.
/// </summary>
/// <param name="Latitude">Decimal degrees latitude in the range [-90, 90].</param>
/// <param name="Longitude">Decimal degrees longitude in the range [-180, 180].</param>
public sealed record GeoCoordinateView(double Latitude, double Longitude)
{
    /// <summary>Returns a string formatted as <c>(&lt;latitude&gt;, &lt;longitude&gt;)</c>.</summary>
    public override string ToString() => $"({Latitude}, {Longitude})";

    /// <summary>
    /// Maps a domain <see cref="GeoCoordinate"/> to a <see cref="GeoCoordinateView"/>.
    /// Returns <see langword="null"/> when <paramref name="operand"/> is <see langword="null"/>.
    /// </summary>
    [return: NotNullIfNotNull(nameof(operand))]
    public static GeoCoordinateView? FromGeoCoordinate(GeoCoordinate? operand) => operand;

    /// <summary>
    /// Implicitly converts a domain <see cref="GeoCoordinate"/> to a <see cref="GeoCoordinateView"/>.
    /// Returns <see langword="null"/> when <paramref name="operand"/> is <see langword="null"/>.
    /// </summary>
    [return: NotNullIfNotNull(nameof(operand))]
    public static implicit operator GeoCoordinateView?(GeoCoordinate? operand)
    {
        return operand is null ? null : new(Latitude: operand.Latitude, Longitude: operand.Longitude);
    }
}
