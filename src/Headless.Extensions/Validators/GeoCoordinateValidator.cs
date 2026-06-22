// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Validators;

/// <summary>Validates geographic latitude and longitude values against their valid ranges.</summary>
[PublicAPI]
public static class GeoCoordinateValidator
{
    /// <summary>The minimum valid longitude, in degrees (<c>-180</c>).</summary>
    public const int LongitudeMinValue = -180;

    /// <summary>The maximum valid longitude, in degrees (<c>180</c>).</summary>
    public const int LongitudeMaxValue = 180;

    /// <summary>The minimum valid latitude, in degrees (<c>-90</c>).</summary>
    public const int LatitudeMinValue = -90;

    /// <summary>The maximum valid latitude, in degrees (<c>90</c>).</summary>
    public const int LatitudeMaxValue = 90;

    /// <summary>Determines whether the given <paramref name="latitude"/>/<paramref name="longitude"/> pair is a valid coordinate.</summary>
    /// <param name="latitude">The latitude, in degrees.</param>
    /// <param name="longitude">The longitude, in degrees.</param>
    /// <returns><see langword="true"/> when both values are finite and within range; otherwise <see langword="false"/>.</returns>
    public static bool IsValid(double latitude, double longitude)
    {
        return IsValidLatitude(latitude) && IsValidLongitude(longitude);
    }

    /// <summary>Determines whether <paramref name="longitude"/> is a finite value within <see cref="LongitudeMinValue"/> and <see cref="LongitudeMaxValue"/>.</summary>
    /// <param name="longitude">The longitude, in degrees.</param>
    /// <returns><see langword="true"/> when the value is valid; otherwise <see langword="false"/>.</returns>
    public static bool IsValidLongitude(double longitude)
    {
        return double.IsFinite(longitude) && longitude is >= LongitudeMinValue and <= LongitudeMaxValue;
    }

    /// <summary>Determines whether <paramref name="latitude"/> is a finite value within <see cref="LatitudeMinValue"/> and <see cref="LatitudeMaxValue"/>.</summary>
    /// <param name="latitude">The latitude, in degrees.</param>
    /// <returns><see langword="true"/> when the value is valid; otherwise <see langword="false"/>.</returns>
    public static bool IsValidLatitude(double latitude)
    {
        return double.IsFinite(latitude) && latitude is >= LatitudeMinValue and <= LatitudeMaxValue;
    }
}
