using System.Runtime.InteropServices;
using Primitives;

namespace Framework.BuildingBlocks.Primitives;

[StructLayout(LayoutKind.Auto)]
public readonly partial struct Month : IPrimitive<int>
{
    public static PrimitiveValidationResult Validate(int value)
    {
        return value is < 1 or > 12 ? "Month must be between 1 and 12" : PrimitiveValidationResult.Ok;
    }
}
