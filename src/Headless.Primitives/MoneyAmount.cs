// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;
using Headless.Generator.Primitives;

namespace Headless.Primitives;

/// <summary>
/// A monetary amount represented as a bare <see cref="decimal"/> primitive value, with no currency code.
/// Use <see cref="Money"/> when an amount must travel with its currency code.
/// </summary>
[PublicAPI]
[StructLayout(LayoutKind.Auto)]
public readonly partial struct MoneyAmount : IPrimitive<decimal>
{
    /// <summary>A <see cref="MoneyAmount"/> with a zero amount.</summary>
    public static readonly MoneyAmount Zero = new(0);

    /// <summary>
    /// Returns a copy of this <see cref="MoneyAmount"/> rounded to two decimal places using
    /// <see cref="MidpointRounding.ToEven"/> (banker's rounding).
    /// </summary>
    /// <returns>A new <see cref="MoneyAmount"/> holding the rounded amount.</returns>
    /// <exception cref="InvalidPrimitiveValueException">Thrown when this <see cref="MoneyAmount"/> was never initialized with a value (a default instance).</exception>
    public MoneyAmount GetRounded()
    {
        var rounded = Math.Round(_valueOrThrow, 2, MidpointRounding.ToEven);

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
