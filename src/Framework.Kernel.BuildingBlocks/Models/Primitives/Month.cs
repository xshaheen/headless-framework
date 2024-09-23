using System.Runtime.InteropServices;
using Primitives;

// ReSharper disable once CheckNamespace
namespace Framework.Kernel.Primitives;

[PublicAPI]
[StructLayout(LayoutKind.Auto)]
public readonly partial struct Month : IPrimitive<int>
{
    public static PrimitiveValidationResult Validate(int value)
    {
        return value is < 1 or > 12 ? "Month must be between 1 and 12" : PrimitiveValidationResult.Ok;
    }
}
