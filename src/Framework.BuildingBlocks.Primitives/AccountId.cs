using Primitives;

namespace Framework.BuildingBlocks.Primitives;

/// <summary>Account identifier.</summary>
/// <example>user-1234</example>
public sealed partial class AccountId : IPrimitive<string>
{
    public static PrimitiveValidationResult Validate(string value)
    {
        return value.Length > 0 ? PrimitiveValidationResult.Ok : "Cannot be empty.";
    }
}
