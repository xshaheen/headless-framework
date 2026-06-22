// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable CA2225, IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Headless.Primitives;

/// <summary>
/// API response envelope for scalar primitive values (strings, numbers, booleans, etc.).
/// Serializes as <c>{ "data": ... }</c>. Prefer <see cref="DataEnvelop{T}"/> for object/record
/// types; use this type when the wrapped value is a value type or a primitive.
/// </summary>
/// <typeparam name="T">The scalar or value type being wrapped.</typeparam>
/// <param name="Data">The wrapped value.</param>
public sealed record ValueEnvelop<T>(T Data)
{
    /// <summary>Implicitly wraps <paramref name="operand"/> in a <see cref="ValueEnvelop{T}"/>.</summary>
    public static implicit operator ValueEnvelop<T>(T operand) => new(operand);
}
