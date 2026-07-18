// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Validators;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Api.Contracts;

/// <summary>FluentValidation rule-builder extensions for <see cref="GeoCoordinateRequest"/>.</summary>
[PublicAPI]
public static class GeoCoordinateValidatorExtensions
{
    /// <summary>
    /// Adds a rule that passes when <paramref name="rule"/>'s value is <see langword="null"/> or
    /// contains valid WGS-84 latitude/longitude values (latitude in [-90, 90], longitude in [-180, 180]).
    /// </summary>
    /// <returns>The rule builder so that additional calls can be chained.</returns>
    public static IRuleBuilderOptions<T, GeoCoordinateRequest?> GeoCoordinate<T>(
        this IRuleBuilder<T, GeoCoordinateRequest?> rule
    )
    {
        return rule.Must(x => x is null || GeoCoordinateValidator.IsValid(x.Latitude, x.Longitude));
    }
}
