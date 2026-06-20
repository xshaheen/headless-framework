// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Primitives;

/// <summary>
/// API response envelope that wraps a single <typeparamref name="T"/> value.
/// Serializes as <c>{ "data": ... }</c>.
/// </summary>
/// <typeparam name="T">The type of the wrapped value.</typeparam>
/// <param name="Data">The wrapped value.</param>
public sealed record DataEnvelop<T>(T Data)
{
    /// <summary>Implicitly wraps <paramref name="operand"/> in a <see cref="DataEnvelop{T}"/>.</summary>
    public static implicit operator DataEnvelop<T>(T operand) => new(operand);

    /// <summary>Returns this envelope; provided for symmetry with factory patterns.</summary>
    public DataEnvelop<T> FromT() => this;
}
