using Primitives;

#pragma warning disable CA1036 // Override methods on comparable types
namespace Framework.Kernel.Primitives;

/// <summary>User identifier.</summary>
[PublicAPI]
public sealed partial class UserId : IPrimitive<string>
{
    public static PrimitiveValidationResult Validate(string value)
    {
        return value.Length > 0 ? PrimitiveValidationResult.Ok : "Cannot be empty.";
    }
}
