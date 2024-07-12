using Primitives;

namespace Framework.BuildingBlocks.Primitives;

/// <summary>Account identifier.</summary>
/// <example>user-1234</example>
#pragma warning disable CA1036 // Override methods on comparable types
public sealed partial class AccountId : IPrimitive<string>
#pragma warning restore CA1036 // Override methods on comparable types
{
    public static PrimitiveValidationResult Validate(string value)
    {
        return value.Length > 0 ? PrimitiveValidationResult.Ok : "Cannot be empty.";
    }
}
