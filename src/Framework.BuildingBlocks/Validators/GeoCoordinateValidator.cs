namespace Framework.BuildingBlocks.Validators;

public static class GeoCoordinateValidator
{
    public static bool IsValid(double latitude, double longitude)
    {
        return IsValidLatitude(latitude) && IsValidLongitude(longitude);
    }

    public static bool IsValidLongitude(double longitude)
    {
        return double.IsFinite(longitude) && longitude is >= -180 and <= 180;
    }

    public static bool IsValidLatitude(double latitude)
    {
        return double.IsFinite(latitude) && latitude is >= -90 and <= 90;
    }
}
