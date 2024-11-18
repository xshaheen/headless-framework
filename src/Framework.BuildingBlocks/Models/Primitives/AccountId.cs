// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Generator.Primitives;

// ReSharper disable once CheckNamespace
namespace Framework.Primitives;

/// <summary>Account identifier.</summary>
/// <example>user-1234</example>
[PublicAPI]
#pragma warning disable CA1036 // Override methods on comparable types
public sealed partial class AccountId : IPrimitive<string>
{
    public static PrimitiveValidationResult Validate(string value)
    {
        return value.Length > 0 ? PrimitiveValidationResult.Ok : "Cannot be empty.";
    }
}
