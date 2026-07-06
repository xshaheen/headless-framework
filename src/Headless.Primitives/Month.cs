// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;
using Headless.Generator.Primitives;

namespace Headless.Primitives;

/// <summary>A calendar month represented as an <see cref="int"/> primitive value in the inclusive range 1–12.</summary>
[PublicAPI]
[StructLayout(LayoutKind.Auto)]
public readonly partial struct Month : IPrimitive<int>
{
    /// <summary>Validates that the month number is within the inclusive range 1–12.</summary>
    /// <param name="value">The candidate month number.</param>
    /// <returns>
    /// <see cref="PrimitiveValidationResult.Ok"/> when <paramref name="value"/> is between 1 and 12 inclusive;
    /// otherwise a failed result carrying the error message.
    /// </returns>
    public static PrimitiveValidationResult Validate(int value)
    {
        return value is < 1 or > 12 ? "Month must be between 1 and 12" : PrimitiveValidationResult.Ok;
    }
}
