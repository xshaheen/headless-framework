// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel.DataAnnotations.Schema;

namespace Headless.Primitives;

/// <summary>A lightweight geographic location identified solely by its latitude and longitude.</summary>
[PublicAPI]
[ComplexType]
public sealed class GeoCoordinate : IEquatable<GeoCoordinate>
{
    /// <summary>The latitude of the location, in degrees. Must be a finite value in the range [-90, 90].</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the value is not finite or falls outside [-90, 90].</exception>
    public required double Latitude
    {
        get;
        init
        {
            if (!double.IsFinite(value) || value is > 90.0 or < -90.0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "Argument `Latitude` must be a finite value in range of -90 to 90"
                );
            }

            field = value;
        }
    }

    /// <summary>The longitude of the location, in degrees. Must be a finite value in the range [-180, 180].</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the value is not finite or falls outside [-180, 180].</exception>
    public required double Longitude
    {
        get;
        init
        {
            if (!double.IsFinite(value) || value is > 180.0 or < -180.0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "Argument `Longitude` must be a finite value in range of -180 to 180"
                );
            }

            field = value;
        }
    }

    /// <summary>Determines whether this coordinate equals <paramref name="other"/> by latitude and longitude.</summary>
    /// <param name="other">The coordinate to compare with.</param>
    /// <returns><see langword="true"/> if both coordinates are equal; otherwise, <see langword="false"/>.</returns>
    public bool Equals(GeoCoordinate? other)
    {
        if (other is null)
        {
            return false;
        }

        return ReferenceEquals(this, other) || (Latitude.Equals(other.Latitude) && Longitude.Equals(other.Longitude));
    }

    /// <summary>Determines whether <paramref name="obj"/> is a <see cref="GeoCoordinate"/> equal to this instance.</summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns><see langword="true"/> if <paramref name="obj"/> is an equal coordinate; otherwise, <see langword="false"/>.</returns>
    public override bool Equals(object? obj)
    {
        return ReferenceEquals(this, obj) || (obj is GeoCoordinate other && Equals(other));
    }

    /// <summary>Returns a hash code derived from the latitude and longitude.</summary>
    /// <returns>A hash code for the current coordinate.</returns>
    public override int GetHashCode()
    {
        return HashCode.Combine(Latitude, Longitude);
    }

    /// <summary>Returns the coordinate formatted as <c>(lat={Latitude}, long={Longitude})</c> using the invariant culture.</summary>
    public override string ToString()
    {
        // string.Create formats the interpolated handler directly with the provider, avoiding the FormattableString allocation.
        return string.Create(CultureInfo.InvariantCulture, $"(lat={Latitude}, long={Longitude})");
    }

    /// <summary>Determines whether two coordinates are equal.</summary>
    /// <param name="left">The first coordinate to compare.</param>
    /// <param name="right">The second coordinate to compare.</param>
    /// <returns><see langword="true"/> if the coordinates are equal (including both being <see langword="null"/>); otherwise, <see langword="false"/>.</returns>
    public static bool operator ==(GeoCoordinate? left, GeoCoordinate? right)
    {
        return left?.Equals(right) ?? right is null;
    }

    /// <summary>Determines whether two coordinates are different.</summary>
    /// <param name="left">The first coordinate to compare.</param>
    /// <param name="right">The second coordinate to compare.</param>
    /// <returns><see langword="true"/> if the coordinates differ; otherwise, <see langword="false"/>.</returns>
    public static bool operator !=(GeoCoordinate? left, GeoCoordinate? right)
    {
        return !(left == right);
    }
}
