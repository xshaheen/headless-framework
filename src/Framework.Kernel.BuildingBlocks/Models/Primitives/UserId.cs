// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Generator.Primitives;

#pragma warning disable CA1036  // Override methods on comparable types
#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
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
