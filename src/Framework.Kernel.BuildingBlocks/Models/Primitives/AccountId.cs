// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Primitives;

#pragma warning disable IDE0130
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
