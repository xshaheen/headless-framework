// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;
using Headless.Generator.Primitives;

namespace Headless.Primitives;

/// <summary>A monetary amount represented as a <see cref="decimal"/> primitive value.</summary>
[PublicAPI]
[StructLayout(LayoutKind.Auto)]
public readonly partial struct Money : IPrimitive<decimal>
{
    /// <summary>A <see cref="Money"/> with a zero amount.</summary>
    public static readonly Money Zero = new(0);

    /// <summary>
    /// Returns a copy of this <see cref="Money"/> rounded to two decimal places using
    /// <see cref="MidpointRounding.ToPositiveInfinity"/>.
    /// </summary>
    /// <returns>A new <see cref="Money"/> holding the rounded amount.</returns>
    /// <exception cref="InvalidPrimitiveValueException">Thrown when this <see cref="Money"/> was never initialized with a value (a default instance).</exception>
    public Money GetRounded()
    {
        var rounded = Math.Round(_valueOrThrow, 2, MidpointRounding.ToPositiveInfinity);

        return new(rounded);
    }

    /// <summary>Validates the monetary amount. Any <see cref="decimal"/> value is accepted.</summary>
    /// <param name="value">The candidate monetary amount.</param>
    /// <returns>Always <see cref="PrimitiveValidationResult.Ok"/>.</returns>
    public static PrimitiveValidationResult Validate(decimal value)
    {
        return PrimitiveValidationResult.Ok;
    }
}
