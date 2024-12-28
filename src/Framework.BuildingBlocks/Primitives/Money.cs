// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;
using Framework.Generator.Primitives;

namespace Framework.Primitives;

[PublicAPI]
[StructLayout(LayoutKind.Auto)]
public readonly partial struct Money : IPrimitive<decimal>
{
    public static readonly Money Zero = new(0);

    public Money GetRounded()
    {
        var rounded = Math.Round(_valueOrThrow, 2, MidpointRounding.ToPositiveInfinity);

        return new(rounded);
    }

    public static PrimitiveValidationResult Validate(decimal value)
    {
        return PrimitiveValidationResult.Ok;
    }
}
