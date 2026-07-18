// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Api.Contracts;

/// <summary>
/// API request contract for a geographic coordinate pair. Use the included <c>GeoCoordinate</c>
/// validator extension to validate that the latitude and longitude
/// values fall within valid WGS-84 ranges before mapping to the domain <see cref="GeoCoordinate"/>
/// primitive.
/// </summary>
/// <param name="Latitude">Decimal degrees latitude in the range [-90, 90].</param>
/// <param name="Longitude">Decimal degrees longitude in the range [-180, 180].</param>
public sealed record GeoCoordinateRequest(double Latitude, double Longitude)
{
    /// <summary>
    /// Returns a culture-invariant string representation formatted as
    /// <c>(lat=&lt;value&gt;, long=&lt;value&gt;)</c>.
    /// </summary>
    public override string ToString()
    {
        FormattableString format = $"(lat={Latitude}, long={Longitude})";

        return format.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Maps this request to the domain <see cref="GeoCoordinate"/> primitive.</summary>
    public GeoCoordinate ToGeoCoordinate()
    {
        return this;
    }

    /// <summary>
    /// Implicitly converts to the domain <see cref="GeoCoordinate"/> primitive.
    /// Returns <see langword="null"/> when <paramref name="operand"/> is <see langword="null"/>.
    /// </summary>
    [return: NotNullIfNotNull(nameof(operand))]
    public static implicit operator GeoCoordinate?(GeoCoordinateRequest? operand)
    {
        return operand is null ? null : new() { Latitude = operand.Latitude, Longitude = operand.Longitude };
    }
}
