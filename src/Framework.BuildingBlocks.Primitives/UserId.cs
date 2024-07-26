using Primitives;

namespace Framework.BuildingBlocks.Primitives;

/// <summary>User identifier.</summary>
[PublicAPI]
public sealed partial class UserId : IPrimitive<string>
{
    public static PrimitiveValidationResult Validate(string value)
    {
        return value.Length > 0 ? PrimitiveValidationResult.Ok : "Cannot be empty.";
    }
}
