// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Generator.Primitives;

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Framework.Kernel.Primitives;

/// <summary>Account identifier.</summary>
/// <example>user-1234</example>
[PublicAPI]
public sealed partial class AccountId : IPrimitive<string>
{
    public static PrimitiveValidationResult Validate(string value)
    {
        return value.Length > 0 ? PrimitiveValidationResult.Ok : "Cannot be empty.";
    }
}
