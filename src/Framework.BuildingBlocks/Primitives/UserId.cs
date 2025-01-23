// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel.DataAnnotations.Schema;
using Framework.Generator.Primitives;

namespace Framework.Primitives;

/// <summary>User identifier.</summary>
[PublicAPI]
[ComplexType]
#pragma warning disable CA1036  // Override methods on comparable types
public sealed partial class UserId : IPrimitive<string>
{
    public static PrimitiveValidationResult Validate(string value)
    {
        return value.Length > 0 ? PrimitiveValidationResult.Ok : "Cannot be empty.";
    }
}
