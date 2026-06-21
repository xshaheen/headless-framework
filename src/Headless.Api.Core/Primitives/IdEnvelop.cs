// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable CA2225, IDE0130
// ReSharper disable once CheckNamespace
namespace Headless.Primitives;

/// <summary>Marker contract for response envelopes that carry a single string identifier.</summary>
public interface IIdEnvelop
{
    /// <summary>The string representation of the resource identifier.</summary>
    string Id { get; }
}

/// <summary>
/// API response envelope that carries a single newly-created or affected resource identifier.
/// Serializes as <c>{ "id": "..." }</c>. Implicit conversions from <see cref="string"/>,
/// <see cref="Guid"/>, <see cref="int"/>, and <see cref="long"/> cover the common ID types.
/// </summary>
/// <param name="Id">The string representation of the resource identifier.</param>
public sealed record IdEnvelop(string Id) : IIdEnvelop
{
    /// <summary>Wraps a string identifier.</summary>
    /// <param name="operand">The string identifier to wrap.</param>
    public static implicit operator IdEnvelop(string operand) => new(operand);

    /// <summary>Wraps a <see cref="Guid"/> identifier using its default string format.</summary>
    /// <param name="operand">The <see cref="Guid"/> identifier to wrap.</param>
    public static implicit operator IdEnvelop(Guid operand) => new(operand.ToString());

    /// <summary>Wraps an <see cref="int"/> identifier formatted with <see cref="CultureInfo.InvariantCulture"/>.</summary>
    /// <param name="operand">The <see cref="int"/> identifier to wrap.</param>
    public static implicit operator IdEnvelop(int operand) => new(operand.ToString(CultureInfo.InvariantCulture));

    /// <summary>Wraps a <see cref="long"/> identifier formatted with <see cref="CultureInfo.InvariantCulture"/>.</summary>
    /// <param name="operand">The <see cref="long"/> identifier to wrap.</param>
    public static implicit operator IdEnvelop(long operand) => new(operand.ToString(CultureInfo.InvariantCulture));
}
