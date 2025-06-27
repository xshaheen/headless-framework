// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Validators;

[PublicAPI]
public static class GeoCoordinateValidator
{
    public const int LongitudeMinValue = -180;
    public const int LongitudeMaxValue = 180;
    public const int LatitudeMinValue = -90;
    public const int LatitudeMaxValue = 90;

    public static bool IsValid(double latitude, double longitude)
    {
        return IsValidLatitude(latitude) && IsValidLongitude(longitude);
    }

    public static bool IsValidLongitude(double longitude)
    {
        return double.IsFinite(longitude) && longitude is >= LongitudeMinValue and <= LongitudeMaxValue;
    }

    public static bool IsValidLatitude(double latitude)
    {
        return double.IsFinite(latitude) && latitude is >= LatitudeMinValue and <= LatitudeMaxValue;
    }
}
