// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel.DataAnnotations.Schema;
using Headless.Generator.Primitives;

namespace Headless.Primitives;

/// <summary>Account identifier.</summary>
/// <example>user-1234</example>
[PublicAPI]
[ComplexType]
#pragma warning disable CA1036 // Override methods on comparable types
public sealed partial class AccountId : IPrimitive<string>
{
    /// <summary>Validates that the account identifier value is non-empty.</summary>
    /// <param name="value">The candidate account identifier value.</param>
    /// <returns>
    /// <see cref="PrimitiveValidationResult.Ok"/> when <paramref name="value"/> is non-empty;
    /// otherwise a failed result carrying the error message.
    /// </returns>
    public static PrimitiveValidationResult Validate(string value)
    {
        return value.Length > 0 ? PrimitiveValidationResult.Ok : "Cannot be empty.";
    }
}
