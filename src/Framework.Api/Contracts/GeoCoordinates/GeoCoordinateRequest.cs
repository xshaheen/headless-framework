// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using FluentValidation;
using Framework.Primitives;
using Framework.Validators;
using NetTopologySuite.Geometries;

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Framework.Api.Contracts;

public sealed record GeoCoordinateRequest(double Latitude, double Longitude)
{
    public override string ToString()
    {
        FormattableString format = $"(lat={Latitude}, long={Longitude})";

        return format.ToString(CultureInfo.InvariantCulture);
    }

    public Coordinate ToCoordinate() => this;

    public GeoCoordinate ToGeoCoordinate() => this;

    [return: NotNullIfNotNull(nameof(operand))]
    public static implicit operator GeoCoordinate?(GeoCoordinateRequest? operand)
    {
        return operand is null ? null : new() { Latitude = operand.Latitude, Longitude = operand.Longitude };
    }

    [return: NotNullIfNotNull(nameof(operand))]
    public static implicit operator Coordinate?(GeoCoordinateRequest? operand)
    {
        return operand is null ? null : new(operand.Longitude, operand.Latitude);
    }
}

public static class GeoCoordinateValidatorExtensions
{
    public static IRuleBuilder<T, GeoCoordinateRequest?> GeoCoordinate<T>(
        this IRuleBuilder<T, GeoCoordinateRequest?> rule
    )
    {
        return rule.Must(x => x is null || GeoCoordinateValidator.IsValid(x.Latitude, x.Longitude));
    }
}
