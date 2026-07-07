// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Primitives;

/// <summary>
/// API response envelope that wraps a single <typeparamref name="T"/> value.
/// Serializes as <c>{ "data": ... }</c>.
/// </summary>
/// <typeparam name="T">The type of the wrapped value.</typeparam>
/// <param name="Data">The wrapped value.</param>
public sealed record DataEnvelope<T>(T Data)
{
    /// <summary>Implicitly wraps <paramref name="operand"/> in a <see cref="DataEnvelope{T}"/>.</summary>
    public static implicit operator DataEnvelope<T>(T operand) => new(operand);

    /// <summary>
    /// Returns this envelope unchanged. Provided as a named factory alternative to the implicit
    /// conversion so callers that cannot rely on implicit casts have a discoverable entry point.
    /// </summary>
    public DataEnvelope<T> FromT() => this;
}
