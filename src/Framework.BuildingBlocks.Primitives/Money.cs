using System.Runtime.InteropServices;
using Primitives;

namespace Framework.BuildingBlocks.Primitives;

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
