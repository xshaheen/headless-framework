// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Framework.FluentValidation.Resources;
using Framework.Primitives;
using Framework.Validators;

namespace Framework.FluentValidation;

[PublicAPI]
public static class GeoValidators
{
    public static IRuleBuilderOptions<T, double> Latitude<T>(this IRuleBuilder<T, double> builder)
    {
        return builder
            .Must(GeoCoordinateValidator.IsValidLatitude)
            .WithErrorDescriptor(FluentValidatorErrorDescriber.Geo.InvalidLatitude());
    }

    public static IRuleBuilderOptions<T, double?> Latitude<T>(this IRuleBuilder<T, double?> builder)
    {
        return builder
            .Must(latitude => latitude is null || GeoCoordinateValidator.IsValidLatitude(latitude.Value))
            .WithErrorDescriptor(FluentValidatorErrorDescriber.Geo.InvalidLatitude());
    }

    public static IRuleBuilderOptions<T, double> Longitude<T>(this IRuleBuilder<T, double> builder)
    {
        return builder
            .Must(GeoCoordinateValidator.IsValidLongitude)
            .WithErrorDescriptor(FluentValidatorErrorDescriber.Geo.InvalidLongitude());
    }

    public static IRuleBuilderOptions<T, double?> Longitude<T>(this IRuleBuilder<T, double?> builder)
    {
        return builder
            .Must(latitude => latitude is null || GeoCoordinateValidator.IsValidLongitude(latitude.Value))
            .WithErrorDescriptor(FluentValidatorErrorDescriber.Geo.InvalidLongitude());
    }
}
